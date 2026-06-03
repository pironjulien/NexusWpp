using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

internal static class HdrDxgiProbe
{
    private static Guid IID_IDXGIFactory1 = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
    private static Guid IID_IDXGIOutput6 = new Guid("068346e8-aaec-4b84-add7-137f513f77a1");
    private const int DXGI_ERROR_NOT_FOUND = unchecked((int)0x887A0002);

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr ppFactory);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int QueryInterfaceDelegate(IntPtr self, ref Guid riid, out IntPtr ppvObject);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint ReleaseDelegate(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1Delegate(IntPtr self, uint adapter, out IntPtr ppAdapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int AdapterEnumOutputsDelegate(IntPtr self, uint output, out IntPtr ppOutput);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int AdapterGetDesc1Delegate(IntPtr self, out DXGI_ADAPTER_DESC1 desc);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Output6GetDesc1Delegate(IntPtr self, out DXGI_OUTPUT_DESC1 desc);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public UIntPtr DedicatedVideoMemory;
        public UIntPtr DedicatedSystemMemory;
        public UIntPtr SharedSystemMemory;
        public LUID AdapterLuid;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_OUTPUT_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        public RECT DesktopCoordinates;
        [MarshalAs(UnmanagedType.Bool)]
        public bool AttachedToDesktop;
        public uint Rotation;
        public IntPtr Monitor;
        public uint BitsPerColor;
        public int ColorSpace;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public float[] RedPrimary;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public float[] GreenPrimary;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public float[] BluePrimary;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public float[] WhitePoint;
        public float MinLuminance;
        public float MaxLuminance;
        public float MaxFullFrameLuminance;
    }

    private static T GetMethod<T>(IntPtr comObject, int index) where T : class
    {
        IntPtr vtbl = Marshal.ReadIntPtr(comObject);
        IntPtr fn = Marshal.ReadIntPtr(vtbl, index * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer(fn, typeof(T)) as T;
    }

    private static void Release(IntPtr comObject)
    {
        if (comObject == IntPtr.Zero) return;
        try
        {
            GetMethod<ReleaseDelegate>(comObject, 2)(comObject);
        }
        catch {}
    }

    public static int Main()
    {
        IntPtr factory = IntPtr.Zero;
        List<string> outputs = new List<string>();
        int hr = CreateDXGIFactory1(ref IID_IDXGIFactory1, out factory);
        if (hr < 0 || factory == IntPtr.Zero)
        {
            Console.WriteLine("{\"ok\":false,\"error\":\"CreateDXGIFactory1\",\"hr\":\"0x" + hr.ToString("x8", CultureInfo.InvariantCulture) + "\"}");
            return 1;
        }

        try
        {
            EnumAdapters1Delegate enumAdapters1 = GetMethod<EnumAdapters1Delegate>(factory, 12);
            for (uint adapterIndex = 0; ; adapterIndex++)
            {
                IntPtr adapter = IntPtr.Zero;
                hr = enumAdapters1(factory, adapterIndex, out adapter);
                if (hr == DXGI_ERROR_NOT_FOUND) break;
                if (hr < 0 || adapter == IntPtr.Zero) continue;

                try
                {
                    string adapterName = "unknown";
                    try
                    {
                        DXGI_ADAPTER_DESC1 adapterDesc;
                        AdapterGetDesc1Delegate getAdapterDesc1 = GetMethod<AdapterGetDesc1Delegate>(adapter, 10);
                        if (getAdapterDesc1(adapter, out adapterDesc) >= 0)
                        {
                            adapterName = adapterDesc.Description.TrimEnd('\0');
                        }
                    }
                    catch {}

                    AdapterEnumOutputsDelegate enumOutputs = GetMethod<AdapterEnumOutputsDelegate>(adapter, 7);
                    for (uint outputIndex = 0; ; outputIndex++)
                    {
                        IntPtr output = IntPtr.Zero;
                        hr = enumOutputs(adapter, outputIndex, out output);
                        if (hr == DXGI_ERROR_NOT_FOUND) break;
                        if (hr < 0 || output == IntPtr.Zero) continue;

                        IntPtr output6 = IntPtr.Zero;
                        try
                        {
                            QueryInterfaceDelegate queryInterface = GetMethod<QueryInterfaceDelegate>(output, 0);
                            hr = queryInterface(output, ref IID_IDXGIOutput6, out output6);
                            if (hr < 0 || output6 == IntPtr.Zero)
                            {
                                outputs.Add(OutputJson(adapterIndex, outputIndex, adapterName, "IDXGIOutput6 unavailable", null));
                                continue;
                            }

                            DXGI_OUTPUT_DESC1 desc;
                            Output6GetDesc1Delegate getDesc1 = GetMethod<Output6GetDesc1Delegate>(output6, 27);
                            hr = getDesc1(output6, out desc);
                            if (hr < 0)
                            {
                                outputs.Add(OutputJson(adapterIndex, outputIndex, adapterName, "GetDesc1 failed 0x" + hr.ToString("x8", CultureInfo.InvariantCulture), null));
                                continue;
                            }

                            outputs.Add(OutputJson(adapterIndex, outputIndex, adapterName, "", desc));
                        }
                        finally
                        {
                            Release(output6);
                            Release(output);
                        }
                    }
                }
                finally
                {
                    Release(adapter);
                }
            }
        }
        finally
        {
            Release(factory);
        }

        Console.WriteLine("{\"ok\":true,\"outputs\":[" + string.Join(",", outputs.ToArray()) + "]}");
        return 0;
    }

    private static string OutputJson(uint adapterIndex, uint outputIndex, string adapterName, string error, DXGI_OUTPUT_DESC1? desc)
    {
        if (!desc.HasValue)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{{\"adapterIndex\":{0},\"outputIndex\":{1},\"adapter\":{2},\"error\":{3}}}",
                adapterIndex, outputIndex, Json(adapterName), Json(error));
        }

        DXGI_OUTPUT_DESC1 d = desc.Value;
        bool hdrColorSpace = d.ColorSpace == 12; // DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020
        return string.Format(CultureInfo.InvariantCulture,
            "{{\"adapterIndex\":{0},\"outputIndex\":{1},\"adapter\":{2},\"deviceName\":{3},\"attached\":{4},\"bitsPerColor\":{5},\"colorSpace\":{6},\"colorSpaceName\":{7},\"dxgiHdrColorSpace\":{8},\"minLuminance\":{9},\"maxLuminance\":{10},\"maxFullFrameLuminance\":{11}}}",
            adapterIndex,
            outputIndex,
            Json(adapterName),
            Json(d.DeviceName.TrimEnd('\0')),
            d.AttachedToDesktop ? "true" : "false",
            d.BitsPerColor,
            d.ColorSpace,
            Json(ColorSpaceName(d.ColorSpace)),
            hdrColorSpace ? "true" : "false",
            Number(d.MinLuminance, "F4"),
            Number(d.MaxLuminance, "F1"),
            Number(d.MaxFullFrameLuminance, "F1"));
    }

    private static string Number(float value, string format)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) return "null";
        return value.ToString(format, CultureInfo.InvariantCulture);
    }

    private static string ColorSpaceName(int value)
    {
        switch (value)
        {
            case 0: return "DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P709";
            case 12: return "DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020";
            case 13: return "DXGI_COLOR_SPACE_YCBCR_FULL_G2084_NONE_P2020";
            case 14: return "DXGI_COLOR_SPACE_RGB_STUDIO_G2084_NONE_P2020";
            case 15: return "DXGI_COLOR_SPACE_YCBCR_STUDIO_G2084_LEFT_P2020";
            default: return "DXGI_COLOR_SPACE_" + value.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static string Json(string value)
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
}
