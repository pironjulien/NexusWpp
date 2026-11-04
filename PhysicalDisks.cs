using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DesktopHtmlHost
{
    // One fixed physical disk (internal SSD or hard disk) and the drive letters of its volumes.
    internal sealed class PhysicalDiskInfo
    {
        internal int Number;
        internal string Name = "";
        internal bool Rotational;      // hard disk (seek penalty): "HDD", otherwise "SSD"
        internal bool HoldsSystem;     // carries the Windows drive
        internal readonly List<string> Letters = new List<string>(); // "C:", "D:"...
    }

    // Disks and their sensors through the storage IOCTLs Windows answers without administrator
    // rights: the device is opened with no access right (query only, nothing read or written).
    internal static partial class PhysicalDisks
    {
        const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
        const uint IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS = 0x00560000;
        const int StorageDeviceProperty = 0, StorageDeviceSeekPenaltyProperty = 7, StorageDeviceTemperatureProperty = 52;
        const int BusTypeUsb = 7, BusTypeSd = 12, BusTypeMmc = 13, BusTypeVirtual = 14, BusTypeFileBackedVirtual = 15;

        [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static partial SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [LibraryImport("kernel32.dll", EntryPoint = "DeviceIoControl")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static unsafe partial bool DeviceIoControl(SafeFileHandle device, uint code, void* input, int inputSize, void* output, int outputSize, out int returned, IntPtr overlapped);

        static SafeFileHandle? Open(string path)
        {
            // No access right, read/write sharing, open existing: allowed to any user.
            var handle = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (handle.IsInvalid) { handle.Dispose(); return null; }
            return handle;
        }

        static unsafe int Query(SafeFileHandle device, int property, Span<byte> output)
        {
            // STORAGE_PROPERTY_QUERY: PropertyId, QueryType = PropertyStandardQuery, AdditionalParameters.
            Span<int> query = stackalloc int[3];
            query[0] = property;
            fixed (int* input = query)
            fixed (byte* buffer = output)
            {
                return DeviceIoControl(device, IOCTL_STORAGE_QUERY_PROPERTY, input, 12, buffer, output.Length, out int returned, IntPtr.Zero) ? returned : 0;
            }
        }

        // Fixed disks that hold at least one fixed drive letter, the system disk first, then by number.
        // USB, SD, virtual and file-backed disks (mounted images) are left out, except the disk
        // Windows runs from (a virtual machine's system disk).
        internal static List<PhysicalDiskInfo> Discover(string systemDrive)
        {
            var disks = new Dictionary<int, PhysicalDiskInfo>();
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed) continue;
                string letter = drive.Name.TrimEnd('\\');
                bool system = letter.Equals(systemDrive, StringComparison.OrdinalIgnoreCase);
                foreach (int number in VolumeDisks(letter))
                {
                    if (!disks.TryGetValue(number, out var disk))
                    {
                        disk = Describe(number, system);
                        if (disk == null) continue;
                        disks[number] = disk;
                    }
                    if (!disk.Letters.Contains(letter, StringComparer.OrdinalIgnoreCase)) disk.Letters.Add(letter);
                    if (system) disk.HoldsSystem = true;
                }
            }
            foreach (var disk in disks.Values) disk.Letters.Sort(StringComparer.OrdinalIgnoreCase);
            return disks.Values.OrderByDescending(d => d.HoldsSystem).ThenBy(d => d.Number).ToList();
        }

        // Physical disk numbers under a volume (several for a spanned volume).
        static unsafe List<int> VolumeDisks(string letter)
        {
            var numbers = new List<int>();
            using var volume = Open(@"\\.\" + letter);
            if (volume == null) return numbers;
            // VOLUME_DISK_EXTENTS: NumberOfDiskExtents (8 bytes with padding), then 24-byte DISK_EXTENT.
            Span<byte> buffer = stackalloc byte[8 + 24 * 8];
            fixed (byte* output = buffer)
            {
                if (!DeviceIoControl(volume, IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS, null, 0, output, buffer.Length, out _, IntPtr.Zero)) return numbers;
            }
            int count = Math.Min(8, BitConverter.ToInt32(buffer));
            for (int i = 0; i < count; i++)
            {
                int number = BitConverter.ToInt32(buffer.Slice(8 + i * 24));
                if (!numbers.Contains(number)) numbers.Add(number);
            }
            return numbers;
        }

        static PhysicalDiskInfo? Describe(int number, bool system)
        {
            using var device = Open(@"\\.\PhysicalDrive" + number);
            if (device == null) return null;
            // STORAGE_DEVICE_DESCRIPTOR: RemovableMedia at 10, VendorIdOffset 12, ProductIdOffset 16, BusType 28.
            Span<byte> descriptor = stackalloc byte[1024];
            int size = Query(device, StorageDeviceProperty, descriptor);
            if (size < 32) return null;
            int bus = BitConverter.ToInt32(descriptor.Slice(28));
            if (!system && (descriptor[10] != 0 || bus == BusTypeUsb || bus == BusTypeSd || bus == BusTypeMmc || bus == BusTypeVirtual || bus == BusTypeFileBackedVirtual)) return null;
            string vendor = Text(descriptor, BitConverter.ToInt32(descriptor.Slice(12)), size);
            string product = Text(descriptor, BitConverter.ToInt32(descriptor.Slice(16)), size);
            string name = product.StartsWith(vendor, StringComparison.OrdinalIgnoreCase) || vendor.Length == 0 ? product : vendor + " " + product;
            // DEVICE_SEEK_PENALTY_DESCRIPTOR: Version, Size, IncursSeekPenalty (BOOLEAN at 8).
            Span<byte> penalty = stackalloc byte[12];
            bool rotational = Query(device, StorageDeviceSeekPenaltyProperty, penalty) >= 9 && penalty[8] != 0;
            return new PhysicalDiskInfo { Number = number, Name = name, Rotational = rotational };
        }

        static string Text(ReadOnlySpan<byte> buffer, int offset, int size)
        {
            if (offset <= 0 || offset >= size) return "";
            var text = buffer.Slice(offset, size - offset);
            int end = text.IndexOf((byte)0);
            return System.Text.Encoding.ASCII.GetString(end < 0 ? text : text.Slice(0, end)).Trim();
        }

        // Temperature the drive reports for itself (NVMe composite, SATA SMART), as Task Manager
        // shows it; -1 when the drive or its driver gives none.
        internal static int Temperature(int number)
        {
            using var device = Open(@"\\.\PhysicalDrive" + number);
            if (device == null) return -1;
            Span<byte> data = stackalloc byte[256];
            return ParseTemperature(data.Slice(0, Query(device, StorageDeviceTemperatureProperty, data)));
        }

        // STORAGE_TEMPERATURE_DATA_DESCRIPTOR: InfoCount at 12, then STORAGE_TEMPERATURE_INFO (16 bytes)
        // from 24; the first one (index 0) is the drive's own reading.
        internal static int ParseTemperature(ReadOnlySpan<byte> data)
        {
            if (data.Length < 40 || BitConverter.ToUInt16(data.Slice(12)) == 0) return -1;
            int celsius = BitConverter.ToInt16(data.Slice(26));
            return celsius > 0 && celsius < 120 ? celsius : -1;
        }
    }
}
