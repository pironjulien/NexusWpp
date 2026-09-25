using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Diagnostics;
using Microsoft.Win32;
using System.Management;
using System.Net.NetworkInformation;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Threading;

namespace DesktopHtmlHost
{
    public class TelemetryCollector
    {
        // Static / Boot cached specs
        public string MotherboardInfo = "";
        public string CpuInfo = "";
        public string CpuL2Cache = "";
        public string CpuL3Cache = "";
        public int CpuCores = 0;
        public int CpuLogical = 0;
        public string CpuBaseSpeed = "0.00";
        public int TotalRamGb = 0;
        public int RamSpeedMts = 0;
        public string RamType = "";
        public int RamModules = 0;
        public string NetworkName = "";
        public string IgpuInfo = "";
        public string IgpuDriver = "";
        public string IgpuDriverDate = "";
        public string DgpuDriver = "";
        public string DgpuDriverDate = "";
        public string GpuName = "";
        public string NpuName = "";
        public string NpuDriver = "";
        public string NpuDriverDate = "";
        public bool NpuDetected = false;
        public string DiskName = "";

        private string igpuLuid = "";
        private string dgpuLuid = "";
        private string npuLuid = "";
        private readonly List<string> directxAdapterLuids = new List<string>();
        private double cpuBaseSpeedVal = 3.80;

        // Active power plans cached list
        public class PowerPlanInfo
        {
            public string Guid;
            public string Name;
            public bool Active;
        }
        public List<PowerPlanInfo> PowerPlans = new List<PowerPlanInfo>();

        // Historical rates
        private ulong prevIdleTime = 0;
        private ulong prevSystemTime = 0;
        private long prevRxBytes = 0;
        private long prevTxBytes = 0;
        private DateTime prevNetTime = DateTime.MinValue;
        private int pingTime = -1;
        private DateTime lastPingTime = DateTime.MinValue;
        private bool isPingPending = false;

        // Process lists and cached top processes
        private string cachedTopProcessesJson = "[]";
        private string cachedTopRamProcessJson = "{}";
        private int topProcessCounter = 0;
        private bool topProcessUpdatePending = false;

        // Network rates cache
        private long cachedNetRxRate = 0;
        private long cachedNetTxRate = 0;
        private string cachedActiveIp = "127.0.0.1";
        private string cachedActiveIpv6 = "fe80::1";
        private string cachedActiveNetName = "Ethernet";
        private string cachedNetType = "Ethernet";
        private long cachedNetLinkSpeedMbps = 1000;

        // Slow perf counters cached separately so the UI can receive 2Hz payloads
        // without running every WMI query twice per second.
        private DateTime lastMemoryPerfTime = DateTime.MinValue;
        private long cachedRamCachedBytes = 0;
        private long cachedRamPoolPagedBytes = 0;
        private long cachedRamPoolNonPagedBytes = 0;
        private long cachedRamActivityVal = 0;
        private DateTime lastSystemPerfTime = DateTime.MinValue;
        private int cachedThreadsCount = 1200;
        private int cachedProcessesCount = 180;
        private DateTime lastGpuPerfTime = DateTime.MinValue;
        private int cachedIgpuUtil = 0;
        private long cachedIgpuMemBytes = 0;
        private int cachedNpuUtil = 0;
        private long cachedNpuMemBytes = 0;
        private int cachedDgpuUtil = 0;
        private long cachedDgpuMemBytes = 0;
        private int cachedIgpuDecodeUtil = 0;

        // NVIDIA telemetry is queried out of process so a faulty display driver cannot corrupt
        // the wallpaper host. Windows performance counters remain the fast utilization source.
        private readonly NvidiaTelemetry nvidiaTelemetry = new NvidiaTelemetry();
        private bool gpuCountersFresh;
        private DateTime lastGpuAttempt = DateTime.MinValue;

        // Wi-Fi signal cache
        private bool wlanUnavailable = false;
        private DateTime lastWifiSignalTime = DateTime.MinValue;
        private int cachedWifiSignal = -1;

        // Real sensors state
        private long dgpuVramTotalBytes = 0;
        private bool cpuThermalPerfUnavailable = false;
        private bool cpuThermalAcpiUnavailable = false;
        private DateTime lastCpuTempTime = DateTime.MinValue;
        private int cachedCpuTempC = -1;
        private string cpuTemperatureStatus = "unavailable";
        private double cachedCpuFreqGhz = 0.0;
        public void Initialize()
        {
            // Read GPUs AdapterLuid and names from Registry
            try
            {
                using (var rootKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\DirectX"))
                {
                    if (rootKey != null)
                    {
                        foreach (var subkeyName in rootKey.GetSubKeyNames())
                        {
                            using (var subkey = rootKey.OpenSubKey(subkeyName))
                            {
                                if (subkey != null)
                                {
                                    object descVal = subkey.GetValue("Description");
                                    string desc = descVal != null ? descVal.ToString() : "";
                                    object luidValObj = subkey.GetValue("AdapterLuid");
                                    if (luidValObj != null && !string.IsNullOrEmpty(desc))
                                    {
                                        long luidVal = Convert.ToInt64(luidValObj);
                                        uint low = (uint)(luidVal & 0xFFFFFFFF);
                                        uint high = (uint)((luidVal >> 32) & 0xFFFFFFFF);
                                        string formattedLuid = string.Format("0x{0:x8}_0x{1:x8}", high, low);
                                        if (!directxAdapterLuids.Contains(formattedLuid))
                                        {
                                            directxAdapterLuids.Add(formattedLuid);
                                        }
                                        
                                        string descLower = desc.ToLowerInvariant();
                                        if (descLower.Contains("nvidia"))
                                        {
                                            dgpuLuid = formattedLuid;
                                        }
                                        else if (descLower.Contains("intel") || descLower.Contains("uhd") || descLower.Contains("arc") ||
                                                 descLower.Contains("qualcomm") || descLower.Contains("adreno"))
                                        {
                                            igpuLuid = formattedLuid;
                                        }
                                        else if (descLower.Contains("radeon") || descLower.Contains("amd"))
                                        {
                                            if (IsIntegratedAmdGpuName(descLower))
                                            {
                                                if (string.IsNullOrEmpty(igpuLuid)) igpuLuid = formattedLuid;
                                            }
                                            else if (string.IsNullOrEmpty(dgpuLuid))
                                            {
                                                dgpuLuid = formattedLuid;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Program.LogDebug("DirectX Registry read error: " + ex.Message);
            }

            // Query CPU, Motherboard, RAM, GPU from WMI
            try
            {
                // Motherboard
                using (var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        object mfgVal = obj["Manufacturer"];
                        object prodVal = obj["Product"];
                        string mfg = mfgVal != null ? mfgVal.ToString() : "";
                        string prod = prodVal != null ? prodVal.ToString() : "";
                        mfg = mfg.Replace(" Co., Ltd.", "").Replace("ASUSTeK COMPUTER INC.", "ASUS");
                        MotherboardInfo = string.Format("{0} ({1})", mfg, prod);
                        break;
                    }
                }

                // CPU
                using (var searcher = new ManagementObjectSearcher("SELECT Name, L2CacheSize, L3CacheSize, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        object cpuNameVal = obj["Name"];
                        if (cpuNameVal != null) CpuInfo = cpuNameVal.ToString().Trim();
                        uint l2 = (uint)(obj["L2CacheSize"] ?? 0);
                        uint l3 = (uint)(obj["L3CacheSize"] ?? 0);
                        CpuL2Cache = string.Format("{0:F1} Mo", l2 / 1024.0);
                        CpuL3Cache = string.Format("{0:F1} Mo", l3 / 1024.0);
                        CpuCores = Convert.ToInt32(obj["NumberOfCores"] ?? CpuCores);
                        CpuLogical = Convert.ToInt32(obj["NumberOfLogicalProcessors"] ?? CpuLogical);
                        uint maxClock = (uint)(obj["MaxClockSpeed"] ?? 0);
                        CpuBaseSpeed = (maxClock / 1000.0).ToString("F2", CultureInfo.InvariantCulture);
                        break;
                    }
                }
                double.TryParse(CpuBaseSpeed, NumberStyles.Any, CultureInfo.InvariantCulture, out cpuBaseSpeedVal);

                // RAM
                using (var searcher = new ManagementObjectSearcher("SELECT Capacity, Speed, SMBIOSMemoryType FROM Win32_PhysicalMemory"))
                {
                    ulong totalCapacity = 0;
                    uint speed = 0;
                    int modules = 0;
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        totalCapacity += (ulong)(obj["Capacity"] ?? 0);
                        speed = (uint)(obj["Speed"] ?? speed);
                        modules++;
                        if (string.IsNullOrEmpty(RamType))
                        {
                            int smbiosType = Convert.ToInt32(obj["SMBIOSMemoryType"] ?? 0);
                            RamType = MemoryTypeFromSmbios(smbiosType);
                        }
                    }
                    if (totalCapacity > 0)
                    {
                        TotalRamGb = (int)Math.Round(totalCapacity / (1024.0 * 1024.0 * 1024.0));
                    }
                    if (speed > 0)
                    {
                        RamSpeedMts = (int)speed;
                    }
                    RamModules = modules;
                }

                // GPUs
                using (var searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion, DriverDate FROM Win32_VideoController"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        object nameVal = obj["Name"];
                        object driverVerVal = obj["DriverVersion"];
                        object dateRawVal = obj["DriverDate"];
                        string name = nameVal != null ? nameVal.ToString() : "";
                        string driverVer = driverVerVal != null ? driverVerVal.ToString() : "";
                        string dateRaw = dateRawVal != null ? dateRawVal.ToString() : "";
                        string driverDate = "";
                        if (dateRaw.Length >= 8)
                        {
                            try
                            {
                                int y = int.Parse(dateRaw.Substring(0, 4));
                                int m = int.Parse(dateRaw.Substring(4, 2));
                                int d = int.Parse(dateRaw.Substring(6, 2));
                                driverDate = string.Format("{0:D2}/{1:D2}/{2:D4}", d, m, y);
                            }
                            catch {}
                        }

                        string nameLower = name.ToLowerInvariant();
                        if (nameLower.Contains("nvidia"))
                        {
                            GpuName = name;
                            DgpuDriver = driverVer;
                            DgpuDriverDate = driverDate;
                        }
                        else if (nameLower.Contains("intel") || nameLower.Contains("arc") || nameLower.Contains("uhd") ||
                                 nameLower.Contains("qualcomm") || nameLower.Contains("adreno"))
                        {
                            IgpuInfo = name;
                            IgpuDriver = driverVer;
                            IgpuDriverDate = driverDate;
                        }
                        else if (nameLower.Contains("radeon") || nameLower.Contains("amd"))
                        {
                            if (IsIntegratedAmdGpuName(nameLower))
                            {
                                if (string.IsNullOrEmpty(IgpuInfo))
                                {
                                    IgpuInfo = name;
                                    IgpuDriver = driverVer;
                                    IgpuDriverDate = driverDate;
                                }
                            }
                            else if (string.IsNullOrEmpty(GpuName))
                            {
                                GpuName = name;
                                DgpuDriver = driverVer;
                                DgpuDriverDate = driverDate;
                            }
                        }
                    }
                }

                using (var searcher = new ManagementObjectSearcher("SELECT DeviceName, DriverVersion, DriverDate FROM Win32_PnPSignedDriver WHERE DeviceName LIKE '%AI Boost%' OR DeviceName LIKE '%Neural%' OR DeviceName LIKE '%VPU%' OR DeviceName LIKE '%Inference%'"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        object nameVal = obj["DeviceName"];
                        object driverVerVal = obj["DriverVersion"];
                        object dateRawVal = obj["DriverDate"];
                        string name = nameVal != null ? nameVal.ToString() : "";
                        if (!IsLikelyNpuDeviceName(name))
                        {
                            continue;
                        }

                        NpuName = name;
                        NpuDetected = true;
                        NpuDriver = driverVerVal != null ? driverVerVal.ToString() : "";

                        string dateRaw = dateRawVal != null ? dateRawVal.ToString() : "";
                        if (dateRaw.Length >= 8)
                        {
                            try
                            {
                                int y = int.Parse(dateRaw.Substring(0, 4));
                                int m = int.Parse(dateRaw.Substring(4, 2));
                                int d = int.Parse(dateRaw.Substring(6, 2));
                                NpuDriverDate = string.Format("{0:D2}/{1:D2}/{2:D4}", d, m, y);
                            }
                            catch {}
                        }
                        break;
                    }
                }

                try
                {
                    string partitionDeviceId = "";
                    using (var searcher = new ManagementObjectSearcher("ASSOCIATORS OF {Win32_LogicalDisk.DeviceID='C:'} WHERE AssocClass=Win32_LogicalDiskToPartition"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            object deviceId = obj["DeviceID"];
                            if (deviceId != null)
                            {
                                partitionDeviceId = deviceId.ToString().Replace("\\", "\\\\").Replace("'", "\\'");
                                break;
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(partitionDeviceId))
                    {
                        string query = string.Format(CultureInfo.InvariantCulture, "ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{0}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition", partitionDeviceId);
                        using (var searcher = new ManagementObjectSearcher(query))
                        {
                            foreach (ManagementObject obj in searcher.Get())
                            {
                                object model = obj["Model"];
                                if (model != null)
                                {
                                    DiskName = model.ToString().Trim();
                                    break;
                                }
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(DiskName))
                    {
                        using (var searcher = new ManagementObjectSearcher("SELECT Model FROM Win32_DiskDrive"))
                        {
                            foreach (ManagementObject obj in searcher.Get())
                            {
                                object model = obj["Model"];
                                if (model != null)
                                {
                                    DiskName = model.ToString().Trim();
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Program.LogDebug("Disk name detection error: " + ex.Message);
                }

                if (!NpuDetected)
                {
                    using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_PnPEntity WHERE Name LIKE '%AI Boost%' OR Name LIKE '%Neural%' OR Name LIKE '%VPU%' OR Name LIKE '%Inference%'"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            object nameVal = obj["Name"];
                            string name = nameVal != null ? nameVal.ToString() : "";
                            if (IsLikelyNpuDeviceName(name))
                            {
                                NpuName = name;
                                NpuDetected = true;
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Program.LogDebug("Telemetry init error: " + ex.Message);
            }

            ReadDgpuVramTotalFromRegistry();

            // Initialize power plans
            UpdatePowerPlansCache();
        }

        private static string MemoryTypeFromSmbios(int smbiosType)
        {
            switch (smbiosType)
            {
                case 20: return "DDR";
                case 21: return "DDR2";
                case 24: return "DDR3";
                case 26: return "DDR4";
                case 27: return "LPDDR";
                case 28: return "LPDDR2";
                case 29: return "LPDDR3";
                case 30: return "LPDDR4";
                case 34: return "DDR5";
                case 35: return "LPDDR5";
                default: return "";
            }
        }

        // AMD APU iGPUs are reported as "AMD Radeon(TM) Graphics" or "Radeon Vega 8 Graphics";
        // discrete boards carry a model number such as "AMD Radeon RX 7800 XT".
        private static bool IsIntegratedAmdGpuName(string lowerName)
        {
            return lowerName.EndsWith("graphics") && !lowerName.Contains(" rx");
        }

        // Reads the dedicated VRAM size reported by the display driver (real hardware value).
        private void ReadDgpuVramTotalFromRegistry()
        {
            if (string.IsNullOrEmpty(GpuName)) return;

            try
            {
                using (var classKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (classKey == null) return;

                    foreach (var subkeyName in classKey.GetSubKeyNames())
                    {
                        using (var subkey = classKey.OpenSubKey(subkeyName))
                        {
                            if (subkey == null) continue;
                            string desc = Convert.ToString(subkey.GetValue("DriverDesc") ?? "");
                            if (string.IsNullOrEmpty(desc)) continue;
                            if (!desc.Equals(GpuName, StringComparison.OrdinalIgnoreCase)) continue;

                            object qw = subkey.GetValue("HardwareInformation.qwMemorySize");
                            if (qw != null)
                            {
                                long bytes = Convert.ToInt64(qw);
                                if (bytes > 0)
                                {
                                    dgpuVramTotalBytes = bytes;
                                    return;
                                }
                            }

                            object dw = subkey.GetValue("HardwareInformation.MemorySize");
                            byte[] raw = dw as byte[];
                            if (raw != null && raw.Length >= 4)
                            {
                                long bytes = BitConverter.ToUInt32(raw, 0);
                                if (bytes > 0)
                                {
                                    dgpuVramTotalBytes = bytes;
                                    return;
                                }
                            }
                            else if (dw != null)
                            {
                                long bytes = Convert.ToInt64(dw);
                                if (bytes > 0)
                                {
                                    dgpuVramTotalBytes = bytes;
                                    return;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Program.LogDebug("VRAM registry read error: " + ex.Message);
            }
        }

        // Real CPU temperature. Tries the thermal zone performance counter first because it is
        // readable without administrator rights, then the ACPI WMI class. Returns -1 when no
        // sensor is exposed so the UI can substitute another real metric.
        private int GetCpuTemperature()
        {
            if (lastCpuTempTime != DateTime.MinValue && (DateTime.Now - lastCpuTempTime).TotalSeconds < 5.0)
            {
                return cachedCpuTempC;
            }
            lastCpuTempTime = DateTime.Now;

            double maxCelsius = double.MinValue;

            if (!cpuThermalPerfUnavailable)
            {
                try
                {
                    using (var searcher = new ManagementObjectSearcher("SELECT Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            double celsius = Convert.ToDouble(obj["Temperature"] ?? 0.0) - 273.15;
                            if (celsius > maxCelsius) maxCelsius = celsius;
                        }
                    }
                }
                catch
                {
                    cpuThermalPerfUnavailable = true;
                }
            }

            if ((maxCelsius <= 5.0 || maxCelsius >= 120.0) && !cpuThermalAcpiUnavailable)
            {
                try
                {
                    using (var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            double deciKelvin = Convert.ToDouble(obj["CurrentTemperature"] ?? 0.0);
                            double celsius = (deciKelvin / 10.0) - 273.15;
                            if (celsius > maxCelsius) maxCelsius = celsius;
                        }
                    }
                }
                catch
                {
                    // Usually access denied without elevation: stop querying this source.
                    cpuThermalAcpiUnavailable = true;
                }
            }

            bool previouslyAvailable = cachedCpuTempC >= 0 || cpuTemperatureStatus == "stale";
            cachedCpuTempC = (maxCelsius > 5.0 && maxCelsius < 120.0) ? (int)Math.Round(maxCelsius) : -1;
            cpuTemperatureStatus = cachedCpuTempC >= 0 ? "fresh" : previouslyAvailable ? "stale" : "unavailable";
            return cachedCpuTempC;
        }

        private static bool IsLikelyNpuDeviceName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            string lower = name.ToLowerInvariant();
            return lower.Contains("ai boost") ||
                   lower.Contains("neural") ||
                   lower.Contains("inference") ||
                   lower.Contains(" vpu") ||
                   lower.Contains("(vpu") ||
                   lower.Equals("npu") ||
                   lower.StartsWith("npu ") ||
                   lower.Contains(" npu ") ||
                   lower.Contains("(npu");
        }

        private string DiscoverNpuLuid()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(@"root\cimv2", "SELECT Name FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine WHERE Name LIKE '%engtype_compute%'"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string name = Convert.ToString(obj["Name"] ?? "");
                        string luid = ExtractLuidFromCounterName(name);
                        if (string.IsNullOrEmpty(luid))
                        {
                            continue;
                        }
                        if (luid.Equals(igpuLuid, StringComparison.OrdinalIgnoreCase) ||
                            luid.Equals(dgpuLuid, StringComparison.OrdinalIgnoreCase) ||
                            directxAdapterLuids.Contains(luid))
                        {
                            continue;
                        }
                        return luid;
                    }
                }
            }
            catch {}

            return "";
        }

        private static string ExtractLuidFromCounterName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "";
            }

            int start = name.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return "";
            }
            start += 5;
            int end = name.IndexOf("_phys_", start, StringComparison.OrdinalIgnoreCase);
            if (end <= start)
            {
                return "";
            }
            return name.Substring(start, end - start).ToLowerInvariant();
        }

        public void UpdatePowerPlansCache()
        {
            try
            {
                var newPlans = new List<PowerPlanInfo>();
                ProcessStartInfo startInfo = new ProcessStartInfo("powercfg", "/list")
                {
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(startInfo))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(2000);
                    string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(line, @"GUID.*:\s*([a-f0-9\-]+)\s*\(([^)]+)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            string guid = match.Groups[1].Value.Trim();
                            string name = match.Groups[2].Value.Trim();
                            bool active = line.Contains("*");
                            newPlans.Add(new PowerPlanInfo { Guid = guid, Name = name, Active = active });
                        }
                    }
                }
                if (newPlans.Count > 0)
                {
                    PowerPlans = newPlans;
                }
            }
            catch (Exception ex)
            {
                Program.LogDebug("UpdatePowerPlansCache error: " + ex.Message);
            }
        }

        public string GetActivePowerPlanGuid()
        {
            try
            {
                IntPtr activeGuidPtr;
                uint res = Win32.PowerGetActiveScheme(IntPtr.Zero, out activeGuidPtr);
                if (res == 0 && activeGuidPtr != IntPtr.Zero)
                {
                    Guid activeGuid = (Guid)Marshal.PtrToStructure(activeGuidPtr, typeof(Guid));
                    Win32.LocalFree(activeGuidPtr);
                    return activeGuid.ToString();
                }
            }
            catch {}

            return "";
        }

        public bool SetActivePowerPlan(string guidStr, out string activeGuid, out string error)
        {
            activeGuid = "";
            error = "";

            Guid requestedGuid;
            if (!Guid.TryParse(guidStr, out requestedGuid))
            {
                error = "invalid GUID";
                activeGuid = GetActivePowerPlanGuid();
                return false;
            }

            if (PowerPlans.Count > 0 && !PowerPlans.Any(p => p.Guid.Equals(guidStr, StringComparison.OrdinalIgnoreCase)))
            {
                UpdatePowerPlansCache();
                if (!PowerPlans.Any(p => p.Guid.Equals(guidStr, StringComparison.OrdinalIgnoreCase)))
                {
                    error = "GUID not found in powercfg /list";
                    activeGuid = GetActivePowerPlanGuid();
                    return false;
                }
            }

            uint result = Win32.PowerSetActiveScheme(IntPtr.Zero, ref requestedGuid);
            if (result != 0)
            {
                error = "PowerSetActiveScheme=" + result.ToString(CultureInfo.InvariantCulture);
            }

            Thread.Sleep(120);
            activeGuid = GetActivePowerPlanGuid();
            bool success = activeGuid.Equals(guidStr, StringComparison.OrdinalIgnoreCase);
            if (success)
            {
                foreach (var plan in PowerPlans)
                {
                    plan.Active = plan.Guid.Equals(activeGuid, StringComparison.OrdinalIgnoreCase);
                }
            }
            else if (string.IsNullOrEmpty(error))
            {
                error = "active scheme did not match request";
            }

            return success;
        }

        // Virtual switches, VM adapters and VPN tunnels must not pollute the displayed identity
        // or double-count traffic that also flows through the physical adapter.
        private static bool IsVirtualOrTunnelAdapter(NetworkInterface ni)
        {
            string text = (ni.Description + " " + ni.Name).ToLowerInvariant();
            return text.Contains("virtual") || text.Contains("vmware") || text.Contains("virtualbox") ||
                   text.Contains("vbox") || text.Contains("hyper-v") || text.Contains("vethernet") ||
                   text.Contains("tap-") || text.Contains("wintun") || text.Contains("wireguard") ||
                   text.Contains("openvpn") || text.Contains("tailscale") || text.Contains("zerotier") ||
                   text.Contains("loopback") || text.Contains("bluetooth");
        }

        private void UpdateNetworkStats()
        {
            try
            {
                long currentRx = 0;
                long currentTx = 0;
                string activeIp = "127.0.0.1";
                string activeIpv6 = "fe80::1";
                string activeNetName = "Ethernet";
                string netType = "Ethernet";
                long linkSpeedMbps = cachedNetLinkSpeedMbps;
                bool identityPinnedToGateway = false;

                NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var ni in interfaces)
                {
                    if (ni.OperationalStatus == OperationalStatus.Up && 
                        ni.NetworkInterfaceType != NetworkInterfaceType.Loopback && 
                        ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                        !IsVirtualOrTunnelAdapter(ni))
                    {
                        var ipProps = ni.GetIPProperties();
                        var stats = ni.GetIPv4Statistics();
                        currentRx += stats.BytesReceived;
                        currentTx += stats.BytesSent;

                        // The interface holding the default gateway is the real internet-facing one.
                        bool hasGateway = ipProps.GatewayAddresses.Count > 0;
                        if (identityPinnedToGateway && !hasGateway)
                        {
                            continue;
                        }

                        bool gotIpv4 = false;
                        foreach (var addr in ipProps.UnicastAddresses)
                        {
                            if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            {
                                activeIp = addr.Address.ToString();
                                activeNetName = ni.Description;
                                netType = (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) ? "Wi-Fi" : "Ethernet";
                                if (ni.Speed > 0)
                                {
                                    linkSpeedMbps = Math.Max(1, ni.Speed / 1000000L);
                                }
                                gotIpv4 = true;
                            }
                            else if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                            {
                                activeIpv6 = addr.Address.ToString();
                            }
                        }

                        if (hasGateway && gotIpv4)
                        {
                            identityPinnedToGateway = true;
                        }
                    }
                }

                if (prevNetTime != DateTime.MinValue)
                {
                    double secElapsed = (DateTime.Now - prevNetTime).TotalSeconds;
                    if (secElapsed > 0.1)
                    {
                        cachedNetRxRate = (long)Math.Max(0, (currentRx - prevRxBytes) / 1024.0 / secElapsed);
                        cachedNetTxRate = (long)Math.Max(0, (currentTx - prevTxBytes) / 1024.0 / secElapsed);
                    }
                }
                prevRxBytes = currentRx;
                prevTxBytes = currentTx;
                prevNetTime = DateTime.Now;

                cachedActiveIp = activeIp;
                cachedActiveIpv6 = activeIpv6;
                cachedActiveNetName = activeNetName;
                cachedNetType = netType;
                cachedNetLinkSpeedMbps = linkSpeedMbps;
            }
            catch {}
        }

        private void UpdatePing()
        {
            if (isPingPending) return;
            if (lastPingTime != DateTime.MinValue && (DateTime.Now - lastPingTime).TotalSeconds < 5.0) return;

            lastPingTime = DateTime.Now;
            isPingPending = true;

            try
            {
                Ping pingSender = new Ping();
                pingSender.PingCompleted += (s, e) =>
                {
                    isPingPending = false;
                    if (e.Reply != null && e.Reply.Status == IPStatus.Success)
                    {
                        pingTime = (int)e.Reply.RoundtripTime;
                    }
                    else
                    {
                        pingTime = -1;
                    }
                    try { ((Ping)s).Dispose(); } catch {}
                };
                pingSender.SendAsync("1.1.1.1", 1000, null);
            }
            catch
            {
                isPingPending = false;
            }
        }

        // Real Wi-Fi signal quality (0-100) of the connected interface through the native WLAN
        // API, which is locale independent. Returns -1 when not connected or unsupported.
        private int GetWifiSignal()
        {
            if (wlanUnavailable) return -1;
            if (lastWifiSignalTime != DateTime.MinValue && (DateTime.Now - lastWifiSignalTime).TotalSeconds < 5.0)
            {
                return cachedWifiSignal;
            }
            lastWifiSignalTime = DateTime.Now;

            IntPtr handle = IntPtr.Zero;
            IntPtr listPtr = IntPtr.Zero;
            try
            {
                uint negotiatedVersion;
                if (WlanApi.WlanOpenHandle(2, IntPtr.Zero, out negotiatedVersion, out handle) != 0)
                {
                    cachedWifiSignal = -1;
                    return -1;
                }
                if (WlanApi.WlanEnumInterfaces(handle, IntPtr.Zero, out listPtr) != 0)
                {
                    cachedWifiSignal = -1;
                    return -1;
                }

                // WLAN_INTERFACE_INFO_LIST layout: dwNumberOfItems(4) + dwIndex(4) + items.
                // WLAN_INTERFACE_INFO layout: GUID(16) + description WCHAR[256](512) + state(4) = 532 bytes.
                int count = Marshal.ReadInt32(listPtr, 0);
                int best = -1;
                for (int i = 0; i < count; i++)
                {
                    IntPtr infoPtr = new IntPtr(listPtr.ToInt64() + 8 + ((long)i * 532));
                    int state = Marshal.ReadInt32(infoPtr, 528);
                    if (state != 1) continue; // wlan_interface_state_connected

                    Guid ifaceGuid = (Guid)Marshal.PtrToStructure(infoPtr, typeof(Guid));
                    uint dataSize;
                    IntPtr dataPtr;
                    // 7 = wlan_intf_opcode_current_connection
                    if (WlanApi.WlanQueryInterface(handle, ref ifaceGuid, 7, IntPtr.Zero, out dataSize, out dataPtr, IntPtr.Zero) == 0)
                    {
                        try
                        {
                            // wlanSignalQuality offset in WLAN_CONNECTION_ATTRIBUTES:
                            // isState(4) + mode(4) + profile(512) + ssid(36) + bssType(4) + bssid(6+2) + phyType(4) + phyIndex(4) = 576
                            if (dataSize >= 580)
                            {
                                int signal = Marshal.ReadInt32(dataPtr, 576);
                                if (signal >= 0 && signal <= 100 && signal > best)
                                {
                                    best = signal;
                                }
                            }
                        }
                        finally
                        {
                            WlanApi.WlanFreeMemory(dataPtr);
                        }
                    }
                }

                cachedWifiSignal = best;
                return best;
            }
            catch
            {
                // wlanapi.dll missing (Server Core) or WLAN service stopped: stop querying.
                wlanUnavailable = true;
                cachedWifiSignal = -1;
                return -1;
            }
            finally
            {
                if (listPtr != IntPtr.Zero)
                {
                    try { WlanApi.WlanFreeMemory(listPtr); } catch {}
                }
                if (handle != IntPtr.Zero)
                {
                    try { WlanApi.WlanCloseHandle(handle, IntPtr.Zero); } catch {}
                }
            }
        }

        private void UpdateTopProcesses()
        {
            topProcessCounter++;
            if (topProcessCounter < 4) return; // run once every 4 seconds
            topProcessCounter = 0;
            if (topProcessUpdatePending) return;
            topProcessUpdatePending = true;

            System.Threading.ThreadPool.QueueUserWorkItem((state) =>
            {
                try
                {
                    var processes = new List<Tuple<string, int, long>>();
                    int cpuCount = Environment.ProcessorCount;
                    if (cpuCount <= 0) cpuCount = 1;

                    using (var searcher = new ManagementObjectSearcher(@"root\cimv2", "SELECT Name, PercentProcessorTime, WorkingSetPrivate FROM Win32_PerfFormattedData_PerfProc_Process WHERE Name <> '_Total' AND Name <> 'Idle'"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            object nameVal = obj["Name"];
                            string name = nameVal != null ? nameVal.ToString() : "";
                            int hashIndex = name.IndexOf('#');
                            if (hashIndex != -1) name = name.Substring(0, hashIndex);

                            int pct = Convert.ToInt32(obj["PercentProcessorTime"] ?? 0);
                            int cpuPercent = (int)Math.Min(99, Math.Round((double)pct / cpuCount));
                            long ramBytes = Convert.ToInt64(obj["WorkingSetPrivate"] ?? 0L);
                            long ramMb = ramBytes / (1024 * 1024);

                            processes.Add(new Tuple<string, int, long>(name, cpuPercent, ramMb));
                        }
                    }

                    var topList = processes.OrderByDescending(p => p.Item2).Take(4).ToList();

                    StringBuilder sb = new StringBuilder();
                    sb.Append("[");
                    for (int i = 0; i < topList.Count; i++)
                    {
                        if (i > 0) sb.Append(",");
                        sb.AppendFormat(CultureInfo.InvariantCulture, "{{\"Name\":{0},\"PercentProcessorTime\":{1},\"WorkingSetPrivate\":{2}}}", 
                            JsonString(topList[i].Item1), topList[i].Item2 * cpuCount, topList[i].Item3 * (long)1024 * 1024);
                    }
                    sb.Append("]");

                    var topRam = processes.OrderByDescending(p => p.Item3).FirstOrDefault();
                    string topRamJson = topRam != null
                        ? string.Format(CultureInfo.InvariantCulture, "{{\"name\":{0},\"ramMb\":{1}}}", JsonString(topRam.Item1), topRam.Item3)
                        : "{}";
                    
                    lock (this)
                    {
                        cachedTopProcessesJson = sb.ToString();
                        cachedTopRamProcessJson = topRamJson;
                    }
                }
                catch (Exception ex)
                {
                    Program.LogDebug("UpdateTopProcesses error: " + ex.Message);
                }
                finally
                {
                    topProcessUpdatePending = false;
                }
            });
        }

        private static string JsonString(string value)
        {
            if (value == null) return "\"\"";
            StringBuilder sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\': sb.Append(@"\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\b': sb.Append(@"\b"); break;
                    case '\f': sb.Append(@"\f"); break;
                    case '\n': sb.Append(@"\n"); break;
                    case '\r': sb.Append(@"\r"); break;
                    case '\t': sb.Append(@"\t"); break;
                    default:
                        if (c < 32)
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public string CollectStats()
        {
            // Gather CPU Load
            int cpuLoad = -1;
            System.Runtime.InteropServices.ComTypes.FILETIME idleTime, kernelTime, userTime;
            if (Win32.GetSystemTimes(out idleTime, out kernelTime, out userTime))
            {
                ulong idle = ((ulong)idleTime.dwHighDateTime << 32) | (uint)idleTime.dwLowDateTime;
                ulong kernel = ((ulong)kernelTime.dwHighDateTime << 32) | (uint)kernelTime.dwLowDateTime;
                ulong user = ((ulong)userTime.dwHighDateTime << 32) | (uint)userTime.dwLowDateTime;
                ulong system = kernel + user;

                if (prevSystemTime > 0)
                {
                    ulong systemDiff = system - prevSystemTime;
                    ulong idleDiff = idle - prevIdleTime;
                    if (systemDiff > 0)
                    {
                        double load = 1.0 - (double)idleDiff / systemDiff;
                        cpuLoad = (int)Math.Max(0, Math.Min(100, Math.Round(load * 100)));
                    }
                }
                prevIdleTime = idle;
                prevSystemTime = system;
            }

            // Real ACPI sensor; -1 when the machine does not expose a thermal zone.
            int cpuTemp = GetCpuTemperature();

            // RAM usage
            int ramLoad = 0;
            ulong ramTotalPhys = 0;
            ulong ramFreePhys = 0;
            ulong ramTotalPage = 0;
            ulong ramAvailPage = 0;
            MEMORYSTATUSEX memStatus = new MEMORYSTATUSEX();
            if (Win32.GlobalMemoryStatusEx(memStatus))
            {
                ramLoad = (int)memStatus.dwMemoryLoad;
                ramTotalPhys = memStatus.ullTotalPhys;
                ramFreePhys = memStatus.ullAvailPhys;
                ramTotalPage = memStatus.ullTotalPageFile;
                ramAvailPage = memStatus.ullAvailPageFile;
            }

            // Disk Space
            double diskFreeGb = 0.0;
            double diskTotalGb = 0.0;
            int diskStoragePercent = 0;
            try
            {
                var drive = new DriveInfo("C");
                diskFreeGb = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                diskTotalGb = drive.TotalSize / (1024.0 * 1024.0 * 1024.0);
                diskStoragePercent = (int)Math.Round((1.0 - (double)drive.AvailableFreeSpace / drive.TotalSize) * 100);
            }
            catch {}

            // Network stats
            UpdateNetworkStats();

            // Query fast WMI metrics
            long commitUsedBytes = (long)(ramTotalPage - ramAvailPage);
            long commitLimitBytes = (long)ramTotalPage;
            long ramCachedBytes = cachedRamCachedBytes;
            long ramPoolPagedBytes = cachedRamPoolPagedBytes;
            long ramPoolNonPagedBytes = cachedRamPoolNonPagedBytes;
            long ramActivityVal = cachedRamActivityVal;
            if (lastMemoryPerfTime == DateTime.MinValue || (DateTime.Now - lastMemoryPerfTime).TotalSeconds >= 2.0)
            {
                try
                {
                    using (var searcher = new ManagementObjectSearcher("SELECT CacheBytes, StandbyCacheNormalPriorityBytes, StandbyCacheReserveBytes, StandbyCacheCoreBytes, PoolPagedBytes, PoolNonpagedBytes, PageFaultsPersec FROM Win32_PerfFormattedData_PerfOS_Memory"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            ulong cache = (ulong)(obj["CacheBytes"] ?? 0);
                            ulong standbyNormal = (ulong)(obj["StandbyCacheNormalPriorityBytes"] ?? 0);
                            ulong standbyReserve = (ulong)(obj["StandbyCacheReserveBytes"] ?? 0);
                            ulong standbyCore = (ulong)(obj["StandbyCacheCoreBytes"] ?? 0);
                            ramCachedBytes = (long)(cache + standbyNormal + standbyReserve + standbyCore);
                            ramPoolPagedBytes = (long)((ulong)(obj["PoolPagedBytes"] ?? 0));
                            ramPoolNonPagedBytes = (long)((ulong)(obj["PoolNonpagedBytes"] ?? 0));
                            ramActivityVal = (long)((uint)(obj["PageFaultsPersec"] ?? 0));
                            cachedRamCachedBytes = ramCachedBytes;
                            cachedRamPoolPagedBytes = ramPoolPagedBytes;
                            cachedRamPoolNonPagedBytes = ramPoolNonPagedBytes;
                            cachedRamActivityVal = ramActivityVal;
                            lastMemoryPerfTime = DateTime.Now;
                            break;
                        }
                    }
                }
                catch {}
            }

            int threadsCount = cachedThreadsCount;
            int processesCount = cachedProcessesCount;
            if (lastSystemPerfTime == DateTime.MinValue || (DateTime.Now - lastSystemPerfTime).TotalSeconds >= 2.0)
            {
                try
                {
                    using (var searcher = new ManagementObjectSearcher("SELECT Threads, Processes FROM Win32_PerfFormattedData_PerfOS_System"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            threadsCount = Convert.ToInt32(obj["Threads"] ?? 0);
                            processesCount = Convert.ToInt32(obj["Processes"] ?? 0);
                            cachedThreadsCount = threadsCount;
                            cachedProcessesCount = processesCount;
                            lastSystemPerfTime = DateTime.Now;
                            break;
                        }
                    }
                }
                catch {}

                // Real average core frequency. The performance percentage is relative to the
                // counter's own ProcessorFrequency base (true base clock on hybrid CPUs), not
                // to the WMI MaxClockSpeed value which can be a different reference.
                try
                {
                    using (var searcher = new ManagementObjectSearcher("SELECT PercentProcessorPerformance, ProcessorFrequency FROM Win32_PerfFormattedData_Counters_ProcessorInformation WHERE Name LIKE '%_Total'"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            double perfPct = Convert.ToDouble(obj["PercentProcessorPerformance"] ?? 0.0);
                            double counterBaseMhz = Convert.ToDouble(obj["ProcessorFrequency"] ?? 0.0);
                            if (perfPct > 0 && counterBaseMhz > 0)
                            {
                                cachedCpuFreqGhz = counterBaseMhz * perfPct / 100.0 / 1000.0;
                            }
                            break;
                        }
                    }
                }
                catch {}
            }

            long diskReadSec = 0;
            long diskWriteSec = 0;
            long diskActiveTime = 0;
            double diskResponse = 0.0;
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT DiskReadBytesPersec, DiskWriteBytesPersec, PercentDiskTime, AvgDiskSecPerTransfer FROM Win32_PerfFormattedData_PerfDisk_LogicalDisk WHERE Name = 'C:'"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        diskReadSec = (long)((ulong)(obj["DiskReadBytesPersec"] ?? 0));
                        diskWriteSec = (long)((ulong)(obj["DiskWriteBytesPersec"] ?? 0));
                        diskActiveTime = (long)((uint)(obj["PercentDiskTime"] ?? 0));
                        diskResponse = Convert.ToDouble(obj["AvgDiskSecPerTransfer"] ?? 0.0);
                        break;
                    }
                }
            }
            catch {}

            double diskReadMbNum = diskReadSec / (1024.0 * 1024.0);
            double diskWriteMbNum = diskWriteSec / (1024.0 * 1024.0);
            double diskThroughputMb = diskReadMbNum + diskWriteMbNum;
            int diskThroughputActivity = diskThroughputMb < 0.05
                ? 0
                : (int)Math.Ceiling(Math.Min(100.0, Math.Sqrt(diskThroughputMb / 2500.0) * 100.0));
            diskActiveTime = Math.Max(0, Math.Min(100, Math.Max(diskActiveTime, diskThroughputActivity)));

            // GPU stats
            int igpuUtil = cachedIgpuUtil;
            long igpuMemBytes = cachedIgpuMemBytes;
            int igpuDecodeUtil = cachedIgpuDecodeUtil;
            int npuUtil = cachedNpuUtil;
            long npuMemBytes = cachedNpuMemBytes;
            int dgpuUtil = cachedDgpuUtil;
            if (lastGpuAttempt == DateTime.MinValue || (DateTime.UtcNow - lastGpuAttempt).TotalSeconds >= 2.0)
            {
                lastGpuAttempt = DateTime.UtcNow;
                gpuCountersFresh = false;
                if (NpuDetected && string.IsNullOrEmpty(npuLuid)) npuLuid = DiscoverNpuLuid();
                var counters = WindowsGpuTelemetry.Read(igpuLuid, dgpuLuid, npuLuid);
                cachedIgpuUtil = igpuUtil = counters[0].Utilization;
                cachedIgpuDecodeUtil = igpuDecodeUtil = counters[0].Decode;
                cachedIgpuMemBytes = igpuMemBytes = counters[0].SharedBytes;
                cachedDgpuUtil = dgpuUtil = counters[1].Utilization;
                cachedDgpuMemBytes = counters[1].DedicatedBytes;
                cachedNpuUtil = npuUtil = counters[2].Utilization;
                cachedNpuMemBytes = npuMemBytes = counters[2].SharedBytes;
                gpuCountersFresh = counters.Any(c => c.Utilization >= 0);
                if (gpuCountersFresh) lastGpuPerfTime = DateTime.UtcNow;
            }

            // NVIDIA metrics are isolated in nvidia-smi. A driver crash can only terminate the
            // short-lived helper process, never this wallpaper process.
            NvidiaSample nv = nvidiaTelemetry.Read(GpuName);
            bool nvidiaStatsSuccess = nv != null;
            int gpuUtil = nv != null && nv.Utilization.HasValue ? (int)Math.Round(nv.Utilization.Value) : dgpuUtil;
            double gpuTemp = nv != null ? nv.Temperature ?? -1 : -1;
            double gpuCoreClock = nv != null ? nv.CoreClock ?? -1 : -1;
            double gpuMemClock = nv != null ? nv.MemoryClock ?? -1 : -1;
            double gpuPowerW = nv != null && nv.PowerWatts.HasValue ? Math.Round(nv.PowerWatts.Value, 1) : -1;
            ulong vramTotal = nv != null && nv.TotalMiB.HasValue ? (ulong)(nv.TotalMiB.Value * 1048576) : (ulong)Math.Max(0, dgpuVramTotalBytes);
            ulong vramUsed = nv != null && nv.UsedMiB.HasValue ? (ulong)(nv.UsedMiB.Value * 1048576) : (ulong)Math.Max(0, cachedDgpuMemBytes);
            bool windowsGpuFresh = gpuCountersFresh && (DateTime.UtcNow - lastGpuPerfTime).TotalSeconds <= 6;
            bool vramFresh = (nv != null && nv.UsedMiB.HasValue) || (windowsGpuFresh && cachedDgpuMemBytes >= 0);
            if (!windowsGpuFresh) {
                igpuUtil = npuUtil = igpuDecodeUtil = -1;
                if (nv == null || !nv.Utilization.HasValue) gpuUtil = -1;
            }

            UpdatePing();
            UpdateTopProcesses();

            bool igpuDetected = !string.IsNullOrEmpty(igpuLuid) || !string.IsNullOrEmpty(IgpuInfo);
            bool dgpuDetected = nvidiaStatsSuccess || !string.IsNullOrEmpty(dgpuLuid) || !string.IsNullOrEmpty(GpuName);

            int wifiSignal = cachedNetType == "Wi-Fi" ? GetWifiSignal() : -1;

            // Real battery state (laptops); flag 128 means no system battery.
            bool batteryPresent = false;
            int batteryPercent = -1;
            bool acLine = true;
            try
            {
                SYSTEM_POWER_STATUS sps = new SYSTEM_POWER_STATUS();
                if (Win32.GetSystemPowerStatus(sps))
                {
                    batteryPresent = (sps.BatteryFlag & 128) == 0 && sps.BatteryFlag != 255;
                    acLine = sps.ACLineStatus == 1;
                    batteryPercent = sps.BatteryLifePercent <= 100 ? sps.BatteryLifePercent : -1;
                    if (batteryPercent < 0)
                    {
                        batteryPresent = false;
                    }
                }
            }
            catch {}

            string activePlanGuidStr = GetActivePowerPlanGuid();

            foreach (var plan in PowerPlans)
            {
                plan.Active = plan.Guid.Equals(activePlanGuidStr, StringComparison.OrdinalIgnoreCase);
            }

            // Generate JSON payload
            StringBuilder sb = new StringBuilder();
            sb.Append("{");
            
            double sampledAt = (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"sampledAt\":{0:F0},\"sources\":{{\"nvidia\":{{\"status\":{1},\"ageMs\":{2}}},\"windowsGpu\":{{\"status\":{3}}},\"cpuTemperature\":{{\"status\":{4},\"label\":\"Zone thermique Windows\"}}}},",
                sampledAt, JsonString(nvidiaTelemetry.Status), nvidiaTelemetry.AgeMs,
                JsonString(windowsGpuFresh ? "fresh" : lastGpuPerfTime == DateTime.MinValue ? "unavailable" : "stale"),
                JsonString(cpuTemperatureStatus));

            // CPU (real frequency from the performance counter; base clock until the first sample arrives)
            string cpuFreqGhzStr = (cachedCpuFreqGhz > 0 ? cachedCpuFreqGhz : cpuBaseSpeedVal).ToString("F2", CultureInfo.InvariantCulture);
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"cpu\":{{\"utilization\":{0},\"temp\":{1},\"name\":{2},\"freqGhz\":\"{3}\",\"baseSpeedGhz\":\"{4}\",\"cores\":{5},\"logical\":{6},\"threads\":{7},\"handles\":0,\"l2Cache\":{8},\"l3Cache\":{9}}},",
                cpuLoad, cpuTemp, JsonString(CpuInfo), cpuFreqGhzStr, CpuBaseSpeed, CpuCores, CpuLogical, threadsCount, JsonString(CpuL2Cache), JsonString(CpuL3Cache));

            // igpu
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"igpu\":{{\"detected\":{0},\"utilization\":{1},\"decodeUtil\":{2},\"usedMb\":{3},\"totalMb\":{4},\"totalGb\":{5},\"name\":{6},\"driver\":{7},\"driverDate\":{8}}},",
                igpuDetected ? "true" : "false", igpuUtil, igpuDecodeUtil, windowsGpuFresh && igpuMemBytes >= 0 ? igpuMemBytes / (1024 * 1024) : -1, ramTotalPhys / 1024 / 1024 / 2, ramTotalPhys / 1024 / 1024 / 1024 / 2, JsonString(IgpuInfo), JsonString(IgpuDriver), JsonString(IgpuDriverDate));

            // npu
            long npuTotalMb = (long)(ramTotalPhys / 1024 / 1024 / 2);
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"npu\":{{\"detected\":{0},\"utilization\":{1},\"usedMb\":{2},\"totalMb\":{3},\"totalGb\":{4},\"name\":{5}}},",
                NpuDetected ? "true" : "false", npuUtil, windowsGpuFresh && npuMemBytes >= 0 ? npuMemBytes / (1024 * 1024) : -1, npuTotalMb, npuTotalMb / 1024, JsonString(NpuName));

            // vram
            int vramPct = vramTotal > 0 ? (int)Math.Round((double)vramUsed / vramTotal * 100) : 0;
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"vram\":{{\"utilization\":{0},\"usedMb\":{1},\"totalMb\":{2},\"totalGb\":{3}}},",
                vramFresh ? vramPct : -1, vramFresh ? (long)(vramUsed / (1024 * 1024)) : -1, vramTotal / (1024 * 1024), vramTotal / 1024 / 1024 / 1024);

            // gpu
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"gpu\":{{\"detected\":{0},\"utilization\":{1},\"temp\":{2},\"coreClock\":{3},\"memoryClock\":{4},\"powerW\":{5},\"name\":{6},\"driver\":{7},\"driverDate\":{8}}},",
                dgpuDetected ? "true" : "false", gpuUtil, gpuTemp, gpuCoreClock, gpuMemClock, gpuPowerW, JsonString(GpuName), JsonString(DgpuDriver), JsonString(DgpuDriverDate));

            // ram
            string commitUsedGbStr = (commitUsedBytes / (1024.0 * 1024.0 * 1024.0)).ToString("F1", CultureInfo.InvariantCulture);
            string commitLimitGbStr = (commitLimitBytes / (1024.0 * 1024.0 * 1024.0)).ToString("F1", CultureInfo.InvariantCulture);
            string cachedGbStr = (ramCachedBytes / (1024.0 * 1024.0 * 1024.0)).ToString("F1", CultureInfo.InvariantCulture);
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"ram\":{{\"utilization\":{0},\"totalGb\":{1},\"commitUsedGb\":\"{2}\",\"commitLimitGb\":\"{3}\",\"cachedGb\":\"{4}\",\"poolPagedMb\":{5},\"poolNonPagedMb\":{6},\"speedMts\":{7},\"activity\":{8},\"type\":{9},\"modules\":{10}}},",
                ramLoad, TotalRamGb, commitUsedGbStr, commitLimitGbStr, cachedGbStr, ramPoolPagedBytes / (1024 * 1024), ramPoolNonPagedBytes / (1024 * 1024), RamSpeedMts, ramActivityVal, JsonString(RamType), RamModules);

            // disk
            string diskFreeGbStr = diskFreeGb.ToString("F1", CultureInfo.InvariantCulture);
            string diskReadMbStr = diskReadMbNum.ToString("F1", CultureInfo.InvariantCulture);
            string diskWriteMbStr = diskWriteMbNum.ToString("F1", CultureInfo.InvariantCulture);
            string diskResponseMsStr = (diskResponse * 1000.0).ToString("F1", CultureInfo.InvariantCulture);
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"disk\":{{\"freeGb\":{0},\"utilization\":{1},\"storagePercent\":{2},\"totalGb\":{3},\"readMb\":\"{4}\",\"writeMb\":\"{5}\",\"responseTimeMs\":{6},\"name\":{7}}},",
                diskFreeGbStr, diskActiveTime, diskStoragePercent, (int)diskTotalGb, diskReadMbStr, diskWriteMbStr, diskResponseMsStr, JsonString(DiskName));

            // network
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"network\":{{\"lan\":{0},\"wifi\":{1},\"ip\":{2},\"ipv6\":{3},\"name\":{4},\"type\":{5},\"linkSpeedMbps\":{6},\"signal\":{7}}},",
                cachedNetTxRate, cachedNetRxRate, JsonString(cachedActiveIp), JsonString(cachedActiveIpv6), JsonString(cachedActiveNetName), JsonString(cachedNetType), cachedNetLinkSpeedMbps, wifiSignal);

            // battery
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"battery\":{{\"present\":{0},\"percent\":{1},\"ac\":{2}}},",
                batteryPresent ? "true" : "false", batteryPercent, acLine ? "true" : "false");

            // global/uptime/ping (real Windows uptime, not the process lifetime)
            sb.AppendFormat(CultureInfo.InvariantCulture, "\"uptime\":{0},\"ping\":{1},\"totalProcesses\":{2},\"motherboard\":{3},",
                Win32.GetTickCount64() / 1000, pingTime, processesCount, JsonString(MotherboardInfo));

            // topProcesses
            string topProcessesStr;
            string topRamProcessStr;
            lock (this)
            {
                topProcessesStr = cachedTopProcessesJson;
                topRamProcessStr = cachedTopRamProcessJson;
            }
            sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "\"topProcesses\":{0},", topProcessesStr);
            sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "\"topRamProcess\":{0},", topRamProcessStr);

            // powerPlans
            sb.Append("\"powerPlans\":[");
            for (int i = 0; i < PowerPlans.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.AppendFormat(CultureInfo.InvariantCulture, "{{\"guid\":{0},\"name\":{1},\"active\":{2}}}",
                    JsonString(PowerPlans[i].Guid), JsonString(PowerPlans[i].Name), PowerPlans[i].Active ? "true" : "false");
            }
            sb.Append("]");

            sb.Append("}");
            return sb.ToString();
        }
    }
}
