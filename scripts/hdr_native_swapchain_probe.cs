using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

internal static class HdrNativeSwapchainProbe
{
    private const int DXGI_ERROR_NOT_FOUND = unchecked((int)0x887A0002);
    private const uint D3D11_SDK_VERSION = 7;
    private const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
    private const int D3D_DRIVER_TYPE_UNKNOWN = 0;
    private const int D3D_FEATURE_LEVEL_11_1 = 0xb100;
    private const int D3D_FEATURE_LEVEL_11_0 = 0xb000;
    private const int D3D_FEATURE_LEVEL_10_1 = 0xa100;
    private const int DXGI_FORMAT_R10G10B10A2_UNORM = 24;
    private const int DXGI_USAGE_RENDER_TARGET_OUTPUT = 0x20;
    private const int DXGI_SWAP_EFFECT_FLIP_DISCARD = 4;
    private const int DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020 = 12;
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    private static Guid IID_IDXGIFactory2 = new Guid("50c83a1c-e072-4c48-87b0-3630fa36a6d0");
    private static Guid IID_IDXGISwapChain3 = new Guid("94d99bdb-f1f8-4ab0-b236-7da0170edab1");
    private static Guid IID_ID3D11Texture2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    private const uint D3DCOMPILE_ENABLE_STRICTNESS = 0x800;

    [DllImport("d3dcompiler_47.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern int D3DCompile(
        string srcData,
        UIntPtr srcDataSize,
        string sourceName,
        IntPtr defines,
        IntPtr include,
        string entrypoint,
        string target,
        uint flags1,
        uint flags2,
        out IntPtr code,
        out IntPtr errorMsgs
    );

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory2(uint flags, ref Guid riid, out IntPtr ppFactory);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDevice(
        IntPtr pAdapter,
        int driverType,
        IntPtr software,
        uint flags,
        int[] featureLevels,
        uint featureLevelsCount,
        uint sdkVersion,
        out IntPtr device,
        out int featureLevel,
        out IntPtr immediateContext
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindow(string className, string windowName);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string windowName);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool MoveWindow(IntPtr hwnd, int x, int y, int width, int height, bool repaint);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr eventHookAssembly, WinEventDelegate eventHookHandle, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr eventHook);

    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int QueryInterfaceDelegate(IntPtr self, ref Guid riid, out IntPtr ppvObject);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint ReleaseDelegate(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1Delegate(IntPtr self, uint adapter, out IntPtr ppAdapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int AdapterGetDesc1Delegate(IntPtr self, out DXGI_ADAPTER_DESC1 desc);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateSwapChainForHwndDelegate(IntPtr self, IntPtr device, IntPtr hwnd, ref DXGI_SWAP_CHAIN_DESC1 desc, IntPtr fullscreenDesc, IntPtr restrictToOutput, out IntPtr swapChain);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int PresentDelegate(IntPtr self, uint syncInterval, uint flags);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetBufferDelegate(IntPtr self, uint buffer, ref Guid riid, out IntPtr surface);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CheckColorSpaceSupportDelegate(IntPtr self, int colorSpace, out uint colorSpaceSupport);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetColorSpace1Delegate(IntPtr self, int colorSpace);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateRenderTargetViewDelegate(IntPtr self, IntPtr resource, IntPtr desc, out IntPtr rtv);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateTexture2DDelegate(IntPtr self, ref D3D11_TEXTURE2D_DESC desc, IntPtr initialData, out IntPtr texture);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void ClearRenderTargetViewDelegate(IntPtr self, IntPtr rtv, [In] float[] colorRGBA);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void UpdateSubresourceDelegate(IntPtr self, IntPtr dstResource, uint dstSubresource, IntPtr dstBox, IntPtr srcData, uint srcRowPitch, uint srcDepthPitch);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateVertexShaderDelegate(IntPtr self, IntPtr bytecode, UIntPtr bytecodeLength, IntPtr classLinkage, out IntPtr vertexShader);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreatePixelShaderDelegate(IntPtr self, IntPtr bytecode, UIntPtr bytecodeLength, IntPtr classLinkage, out IntPtr pixelShader);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateBufferDelegate(IntPtr self, ref D3D11_BUFFER_DESC desc, ref D3D11_SUBRESOURCE_DATA initialData, out IntPtr buffer);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void VSSetShaderDelegate(IntPtr self, IntPtr shader, IntPtr classInstances, uint numClassInstances);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void PSSetShaderDelegate(IntPtr self, IntPtr shader, IntPtr classInstances, uint numClassInstances);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void IASetPrimitiveTopologyDelegate(IntPtr self, uint topology);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void RSSetViewportsDelegate(IntPtr self, uint numViewports, [In] D3D11_VIEWPORT[] viewports);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void OMSetRenderTargetsDelegate(IntPtr self, uint numViews, IntPtr renderTargetViewArray, IntPtr depthStencilView);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void DrawDelegate(IntPtr self, uint vertexCount, uint startVertexLocation);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void PSSetConstantBuffersDelegate(IntPtr self, uint startSlot, uint numBuffers, IntPtr bufferArray);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr GetBufferPointerDelegate(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate UIntPtr GetBufferSizeDelegate(IntPtr self);

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
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DXGI_SWAP_CHAIN_DESC1
    {
        public uint Width;
        public uint Height;
        public int Format;
        [MarshalAs(UnmanagedType.Bool)]
        public bool Stereo;
        public DXGI_SAMPLE_DESC SampleDesc;
        public int BufferUsage;
        public uint BufferCount;
        public int Scaling;
        public int SwapEffect;
        public int AlphaMode;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DXGI_SAMPLE_DESC
    {
        public uint Count;
        public uint Quality;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_TEXTURE2D_DESC
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public int Format;
        public DXGI_SAMPLE_DESC SampleDesc;
        public uint Usage;
        public uint BindFlags;
        public uint CPUAccessFlags;
        public uint MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_BUFFER_DESC
    {
        public uint ByteWidth;
        public uint Usage;
        public uint BindFlags;
        public uint CPUAccessFlags;
        public uint MiscFlags;
        public uint StructureByteStride;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_SUBRESOURCE_DATA
    {
        public IntPtr pSysMem;
        public uint SysMemPitch;
        public uint SysMemSlicePitch;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_VIEWPORT
    {
        public float TopLeftX;
        public float TopLeftY;
        public float Width;
        public float Height;
        public float MinDepth;
        public float MaxDepth;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public int dwLowDateTime;
        public int dwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShaderConstants
    {
        public float Time;
        public float Width;
        public float Height;
        public float Cpu;
        public float Ram;
        public float Ssd;
        public float Pad0;
        public float Pad1;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
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
        try { GetMethod<ReleaseDelegate>(comObject, 2)(comObject); } catch {}
    }

    [STAThread]
    public static int Main(string[] args)
    {
        int seconds = 6;
        if (args.Length > 0) int.TryParse(args[0], out seconds);
        if (seconds < 1) seconds = 1;

        Application.EnableVisualStyles();
        using (var form = new ProbeForm(seconds))
        {
            Application.Run(form);
            Console.WriteLine(form.ResultJson);
            return form.ExitCode;
        }
    }

    private sealed class ProbeForm : Form
    {
        private readonly int seconds;
        private Timer frameTimer;
        private Timer fullscreenTimer;
        private Timer fullscreenFallbackTimer;
        private IntPtr factory = IntPtr.Zero;
        private IntPtr adapter = IntPtr.Zero;
        private IntPtr device = IntPtr.Zero;
        private IntPtr context = IntPtr.Zero;
        private IntPtr swapChain = IntPtr.Zero;
        private IntPtr swapChain3 = IntPtr.Zero;
        private IntPtr backBufferTexture = IntPtr.Zero;
        private IntPtr renderTargetView = IntPtr.Zero;
        private int swapChainWidth = 0;
        private int swapChainHeight = 0;
        private int sceneWidth = 0;
        private int sceneHeight = 0;
        private IntPtr vertexShader = IntPtr.Zero;
        private IntPtr pixelShader = IntPtr.Zero;
        private IntPtr constantBuffer = IntPtr.Zero;
        private IntPtr rtvArray = IntPtr.Zero;
        private int presentCount = 0;
        private int renderCount = 0;
        private int skippedFrameCount = 0;
        private int suspendCount = 0;
        private int resumeCount = 0;
        private int fullscreenEventCount = 0;
        private int fullscreenForegroundCheckCount = 0;
        private int fullscreenFallbackScanCount = 0;
        private IntPtr fullscreenEventHook = IntPtr.Zero;
        private WinEventDelegate fullscreenEventCallback;
        private bool runtimeSuspended = false;
        private string fullscreenReason = "";
        private DateTime started;
        private DateTime lastTelemetryUpdate = DateTime.MinValue;
        private ulong previousCpuIdle = 0;
        private ulong previousCpuTotal = 0;
        private float telemetryCpu = 0;
        private float telemetryRam = 0;
        private float telemetrySsd = 0;
        private int telemetryUpdateCount = 0;
        private bool initOk = false;
        private string resultAdapterName = "unknown";
        private int resultFeatureLevel = 0;
        private uint resultColorSpaceSupport = 0;
        private bool resultPresentSupport = false;
        private bool resultSetColorSpaceOk = false;
        private bool resultRenderTargetViewOk = false;
        private bool resultGpuShaderOk = false;
        private string resultError = "";
        public string ResultJson = "{\"ok\":false,\"error\":\"not started\"}";
        public int ExitCode = 1;

        public ProbeForm(int seconds)
        {
            this.seconds = seconds;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Bounds = Screen.PrimaryScreen.Bounds;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            started = DateTime.Now;
            UpdateTelemetry(true);
            AttachToDesktop();
            InitializeDxgi();

            frameTimer = new Timer();
            frameTimer.Interval = 100;
            frameTimer.Tick += (s, ev) =>
            {
                try
                {
                    if (runtimeSuspended)
                    {
                        skippedFrameCount++;
                    }
                    else
                    if (swapChain != IntPtr.Zero)
                    {
                        RenderFrame();
                        GetMethod<PresentDelegate>(swapChain, 8)(swapChain, 1, 0);
                        presentCount++;
                    }
                }
                catch {}

                if ((DateTime.Now - started).TotalSeconds >= seconds)
                {
                    frameTimer.Stop();
                    Close();
                }
            };
            frameTimer.Start();

            InstallFullscreenEventHook();
            RefreshFullscreenState(true);

            fullscreenTimer = new Timer();
            fullscreenTimer.Interval = 100;
            fullscreenTimer.Tick += (s, ev) => RefreshFullscreenState(false);
            fullscreenTimer.Start();

            fullscreenFallbackTimer = new Timer();
            fullscreenFallbackTimer.Interval = 2000;
            fullscreenFallbackTimer.Tick += (s, ev) => RefreshFullscreenState(true);
            fullscreenFallbackTimer.Start();
        }

        private void AttachToDesktop()
        {
            IntPtr progman = FindWindow("Progman", null);
            if (progman != IntPtr.Zero)
            {
                IntPtr result;
                SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 100, out result);
            }

            IntPtr workerw = IntPtr.Zero;
            EnumWindows((hwnd, lParam) =>
            {
                IntPtr shellDll = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (shellDll != IntPtr.Zero)
                {
                    workerw = FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null);
                    if (workerw == IntPtr.Zero)
                    {
                        workerw = FindWindowEx(hwnd, IntPtr.Zero, "WorkerW", null);
                    }
                }
                return true;
            }, IntPtr.Zero);

            if (workerw == IntPtr.Zero) workerw = progman;
            if (workerw != IntPtr.Zero)
            {
                SetParent(Handle, workerw);
                int style = GetWindowLong(Handle, -16);
                style |= 0x40000000; // WS_CHILD
                style &= ~unchecked((int)0x80000000); // WS_POPUP
                SetWindowLong(Handle, -16, style);
                SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0002 | 0x0001 | 0x0004 | 0x0020);

                var bounds = SystemInformation.VirtualScreen;
                MoveWindow(Handle, bounds.Left, bounds.Top, bounds.Width, bounds.Height, true);
            }
        }

        private void InitializeDxgi()
        {
            string error = "";
            int hr = 0;
            int featureLevel = 0;
            string adapterName = "unknown";
            uint colorSpaceSupport = 0;
            bool checkPresentSupport = false;
            bool setColorSpaceOk = false;

            try
            {
                hr = CreateDXGIFactory2(0, ref IID_IDXGIFactory2, out factory);
                if (hr < 0 || factory == IntPtr.Zero) throw new Exception("CreateDXGIFactory2 hr=0x" + hr.ToString("x8", CultureInfo.InvariantCulture));

                adapter = SelectAdapter(factory, out adapterName);
                if (adapter == IntPtr.Zero) throw new Exception("No DXGI adapter found");

                int[] levels = new int[] { D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0, D3D_FEATURE_LEVEL_10_1 };
                hr = D3D11CreateDevice(adapter, D3D_DRIVER_TYPE_UNKNOWN, IntPtr.Zero, D3D11_CREATE_DEVICE_BGRA_SUPPORT, levels, (uint)levels.Length, D3D11_SDK_VERSION, out device, out featureLevel, out context);
                if (hr < 0)
                {
                    levels = new int[] { D3D_FEATURE_LEVEL_11_0, D3D_FEATURE_LEVEL_10_1 };
                    hr = D3D11CreateDevice(adapter, D3D_DRIVER_TYPE_UNKNOWN, IntPtr.Zero, D3D11_CREATE_DEVICE_BGRA_SUPPORT, levels, (uint)levels.Length, D3D11_SDK_VERSION, out device, out featureLevel, out context);
                }
                if (hr < 0 || device == IntPtr.Zero) throw new Exception("D3D11CreateDevice hr=0x" + hr.ToString("x8", CultureInfo.InvariantCulture));

                var bounds = SystemInformation.VirtualScreen;
                double scale = bounds.Width > 1280 ? 1280.0 / bounds.Width : 1.0;
                swapChainWidth = Math.Max(1, (int)Math.Round(bounds.Width * scale));
                swapChainHeight = Math.Max(1, (int)Math.Round(bounds.Height * scale));
                DXGI_SWAP_CHAIN_DESC1 desc = new DXGI_SWAP_CHAIN_DESC1();
                desc.Width = (uint)swapChainWidth;
                desc.Height = (uint)swapChainHeight;
                desc.Format = DXGI_FORMAT_R10G10B10A2_UNORM;
                desc.Stereo = false;
                desc.SampleDesc.Count = 1;
                desc.SampleDesc.Quality = 0;
                desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
                desc.BufferCount = 2;
                desc.Scaling = 0;
                desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;
                desc.AlphaMode = 0;
                desc.Flags = 0;

                hr = GetMethod<CreateSwapChainForHwndDelegate>(factory, 15)(factory, device, Handle, ref desc, IntPtr.Zero, IntPtr.Zero, out swapChain);
                if (hr < 0 || swapChain == IntPtr.Zero) throw new Exception("CreateSwapChainForHwnd hr=0x" + hr.ToString("x8", CultureInfo.InvariantCulture));

                hr = GetMethod<QueryInterfaceDelegate>(swapChain, 0)(swapChain, ref IID_IDXGISwapChain3, out swapChain3);
                if (hr < 0 || swapChain3 == IntPtr.Zero) throw new Exception("Query IDXGISwapChain3 hr=0x" + hr.ToString("x8", CultureInfo.InvariantCulture));

                hr = GetMethod<CheckColorSpaceSupportDelegate>(swapChain3, 37)(swapChain3, DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020, out colorSpaceSupport);
                if (hr < 0) throw new Exception("CheckColorSpaceSupport hr=0x" + hr.ToString("x8", CultureInfo.InvariantCulture));
                checkPresentSupport = (colorSpaceSupport & 0x1) != 0;

                hr = GetMethod<SetColorSpace1Delegate>(swapChain3, 38)(swapChain3, DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020);
                setColorSpaceOk = hr >= 0;
                if (!setColorSpaceOk) throw new Exception("SetColorSpace1 hr=0x" + hr.ToString("x8", CultureInfo.InvariantCulture));

                CreateRenderTarget();
                CreateShaders();

                initOk = true;
                resultAdapterName = adapterName;
                resultFeatureLevel = featureLevel;
                resultColorSpaceSupport = colorSpaceSupport;
                resultPresentSupport = checkPresentSupport;
                resultSetColorSpaceOk = setColorSpaceOk;
                resultRenderTargetViewOk = renderTargetView != IntPtr.Zero;
                resultGpuShaderOk = vertexShader != IntPtr.Zero && pixelShader != IntPtr.Zero && constantBuffer != IntPtr.Zero;
                ResultJson = BuildResultJson();
                ExitCode = 0;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                initOk = false;
                resultError = error;
                resultAdapterName = adapterName;
                resultFeatureLevel = featureLevel;
                resultColorSpaceSupport = colorSpaceSupport;
                resultPresentSupport = checkPresentSupport;
                resultSetColorSpaceOk = setColorSpaceOk;
                resultRenderTargetViewOk = renderTargetView != IntPtr.Zero;
                resultGpuShaderOk = vertexShader != IntPtr.Zero && pixelShader != IntPtr.Zero && constantBuffer != IntPtr.Zero;
                ResultJson = BuildResultJson();
                ExitCode = 1;
            }
        }

        private void InstallFullscreenEventHook()
        {
            fullscreenEventCallback = (hook, eventType, hwnd, idObject, idChild, eventThread, eventTime) =>
            {
                System.Threading.Interlocked.Increment(ref fullscreenEventCount);
                try
                {
                    if (!IsDisposed && IsHandleCreated)
                    {
                        BeginInvoke((Action)(() => RefreshFullscreenState(false)));
                    }
                }
                catch {}
            };

            fullscreenEventHook = SetWinEventHook(
                EVENT_SYSTEM_FOREGROUND,
                EVENT_SYSTEM_MINIMIZEEND,
                IntPtr.Zero,
                fullscreenEventCallback,
                0,
                0,
                WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        }

        private void RefreshFullscreenState(bool fullScan)
        {
            string reason;
            bool fullscreen;
            if (fullScan)
            {
                fullscreenFallbackScanCount++;
                fullscreen = IsAnyFullscreenWindow(out reason);
            }
            else
            {
                fullscreenForegroundCheckCount++;
                IntPtr foreground = GetForegroundWindow();
                fullscreen = IsFullscreenCandidate(foreground, out reason);
            }

            SetRuntimeSuspended(fullscreen, reason);
        }

        private void SetRuntimeSuspended(bool suspended, string reason)
        {
            if (runtimeSuspended == suspended) return;
            runtimeSuspended = suspended;
            fullscreenReason = reason ?? "";
            if (suspended) suspendCount++;
            else resumeCount++;
        }

        private bool IsAnyFullscreenWindow(out string reason)
        {
            IntPtr foreground = GetForegroundWindow();
            if (IsFullscreenCandidate(foreground, out reason))
            {
                return true;
            }

            bool found = false;
            string foundReason = "";
            EnumWindows((hwnd, lParam) =>
            {
                if (found) return false;
                string candidateReason;
                if (IsFullscreenCandidate(hwnd, out candidateReason))
                {
                    found = true;
                    foundReason = candidateReason;
                }
                return !found;
            }, IntPtr.Zero);

            reason = foundReason;
            return found;
        }

        private bool IsFullscreenCandidate(IntPtr hwnd, out string reason)
        {
            reason = "";
            if (hwnd == IntPtr.Zero || hwnd == Handle || IsIconic(hwnd) || !IsWindowVisible(hwnd)) return false;

            StringBuilder className = new StringBuilder(256);
            GetClassName(hwnd, className, className.Capacity);
            string cls = className.ToString();
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Button") return false;

            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            if (pid == (uint)Process.GetCurrentProcess().Id) return false;

            string processName = "";
            try
            {
                Process p = Process.GetProcessById((int)pid);
                processName = p.ProcessName.ToLowerInvariant();
                if (processName == "explorer" ||
                    processName == "textinputhost" ||
                    processName == "shellexperiencehost" ||
                    processName == "searchhost" ||
                    processName == "startmenuexperiencehost")
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            RECT rect;
            if (!GetWindowRect(hwnd, out rect)) return false;

            IntPtr monitor = MonitorFromWindow(hwnd, 2);
            if (monitor == IntPtr.Zero) return false;

            MONITORINFO info = new MONITORINFO();
            info.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (!GetMonitorInfo(monitor, ref info)) return false;

            int tolerance = 3;
            bool fullscreen = rect.Left <= info.rcMonitor.Left + tolerance &&
                              rect.Top <= info.rcMonitor.Top + tolerance &&
                              rect.Right >= info.rcMonitor.Right - tolerance &&
                              rect.Bottom >= info.rcMonitor.Bottom - tolerance;
            if (fullscreen)
            {
                reason = string.Format(CultureInfo.InvariantCulture, "{0}/{1} hwnd=0x{2:x}", processName, cls, hwnd.ToInt64());
            }
            return fullscreen;
        }

        private void CreateRenderTarget()
        {
            int hr = GetMethod<GetBufferDelegate>(swapChain, 9)(swapChain, 0, ref IID_ID3D11Texture2D, out backBufferTexture);
            if (hr < 0 || backBufferTexture == IntPtr.Zero) throw new Exception("GetBuffer hr=0x" + hr.ToString("x8", CultureInfo.InvariantCulture));

            hr = GetMethod<CreateRenderTargetViewDelegate>(device, 9)(device, backBufferTexture, IntPtr.Zero, out renderTargetView);
            if (hr < 0 || renderTargetView == IntPtr.Zero) throw new Exception("CreateRenderTargetView hr=0x" + hr.ToString("x8", CultureInfo.InvariantCulture));

            sceneWidth = Math.Max(1, swapChainWidth);
            sceneHeight = Math.Max(1, swapChainHeight);

            rtvArray = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(rtvArray, renderTargetView);
        }

        private void CreateShaders()
        {
            string hlsl = @"
cbuffer FrameConstants : register(b0)
{
    float time;
    float width;
    float height;
    float cpu;
    float ram;
    float ssd;
    float pad0;
    float pad1;
};

struct VSOut
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
};

VSOut VSMain(uint id : SV_VertexID)
{
    float2 pos;
    pos.x = (id == 2) ? 3.0 : -1.0;
    pos.y = (id == 1) ? 3.0 : -1.0;
    VSOut o;
    o.pos = float4(pos, 0.0, 1.0);
    o.uv = float2((pos.x + 1.0) * 0.5, 1.0 - (pos.y + 1.0) * 0.5);
    return o;
}

float boxMask(float2 uv, float2 center, float2 halfSize)
{
    float2 d = abs(uv - center) - halfSize;
    return 1.0 - saturate(max(d.x, d.y) / 0.008);
}

float boxBorder(float2 uv, float2 center, float2 halfSize)
{
    float outer = boxMask(uv, center, halfSize);
    float inner = boxMask(uv, center, max(halfSize - 0.008, 0.0));
    return saturate(outer - inner);
}

float nodeMask(float2 p, float2 center, float radius)
{
    float d = length(p - center);
    return 1.0 - saturate(abs(d - radius) / 0.010);
}

float gaugeArc(float2 uv, float2 center, float radius, float thickness, float value)
{
    float2 q = uv - center;
    float d = length(q);
    float ring = 1.0 - saturate(abs(d - radius) / thickness);
    float angle = atan2(-q.x, q.y);
    float normalized = frac((angle + 3.14159265) / 6.2831853 + 0.08);
    float gap = step(0.08, normalized) * step(normalized, 0.92);
    float progress = step(normalized, 0.08 + saturate(value) * 0.84);
    return ring * gap * progress;
}

float gaugeTrack(float2 uv, float2 center, float radius, float thickness)
{
    float2 q = uv - center;
    float d = length(q);
    float ring = 1.0 - saturate(abs(d - radius) / thickness);
    float angle = atan2(-q.x, q.y);
    float normalized = frac((angle + 3.14159265) / 6.2831853 + 0.08);
    float gap = step(0.08, normalized) * step(normalized, 0.92);
    return ring * gap;
}

float4 PSMain(VSOut input) : SV_Target
{
    float aspect = width / max(height, 1.0);
    float2 uv = input.uv;
    float2 p = uv * 2.0 - 1.0;
    p.x *= aspect;
    float d = length(p);
    float radial = saturate(1.0 - d);
    float pulse = 0.5 + 0.5 * sin(time * 1.8);

    float2 gridUv = uv * float2(32.0, 18.0);
    float2 gridLine = abs(frac(gridUv) - 0.5);
    float grid = step(0.492, max(gridLine.x, gridLine.y)) * 0.045;

    float leftPanel = boxBorder(uv, float2(0.145, 0.50), float2(0.115, 0.42));
    float rightPanel = boxBorder(uv, float2(0.855, 0.50), float2(0.115, 0.42));
    float topPanel = boxBorder(uv, float2(0.50, 0.145), float2(0.245, 0.075));
    float bottomPanel = boxBorder(uv, float2(0.50, 0.855), float2(0.245, 0.075));
    float panels = leftPanel + rightPanel + topPanel + bottomPanel;

    float hub = 1.0 - saturate(abs(d - (0.180 + pulse * 0.018)) / 0.012);
    float hub2 = 1.0 - saturate(abs(d - 0.305) / 0.008);
    float crossGlow = (1.0 - saturate(abs(p.x) / 0.004)) * 0.075 + (1.0 - saturate(abs(p.y) / 0.004)) * 0.075;

    float nodes = 0.0;
    float links = 0.0;
    [unroll]
    for (int i = 0; i < 6; i++)
    {
        float a = 6.2831853 * (i / 6.0) + 0.15 * sin(time * 0.45);
        float2 n = float2(cos(a) * 0.63, sin(a) * 0.46);
        nodes += nodeMask(p, n, 0.045 + 0.008 * sin(time * 1.3 + i));
        float linkDist = abs(cross(float3(normalize(n), 0.0), float3(p, 0.0)).z);
        float along = dot(p, normalize(n));
        links += (1.0 - saturate(linkDist / 0.006)) * step(0.0, along) * step(along, length(n));
    }

    float sweep = step(frac(uv.x * 5.0 + time * 0.20), 0.022) * step(abs(p.y), 0.58) * 0.40;
    float packet = step(frac((uv.x + uv.y) * 8.0 - time * 0.55), 0.018) * step(abs(d - 0.47), 0.035) * 0.32;
    float cpuArc = gaugeArc(uv, float2(0.106, 0.315), 0.040, 0.008, cpu);
    float ramArc = gaugeArc(uv, float2(0.106, 0.500), 0.040, 0.008, ram);
    float ssdArc = gaugeArc(uv, float2(0.106, 0.685), 0.040, 0.008, ssd);
    float gauges = gaugeTrack(uv, float2(0.106, 0.315), 0.040, 0.004)
                 + gaugeTrack(uv, float2(0.106, 0.500), 0.040, 0.004)
                 + gaugeTrack(uv, float2(0.106, 0.685), 0.040, 0.004);

    float3 color = float3(0.007, 0.017, 0.060);
    color += radial * float3(0.030, 0.105, 0.315);
    color += grid * float3(0.015, 0.095, 0.140);
    color += panels * float3(0.060, 0.280, 0.360);
    color += hub * float3(0.140, 0.680, 1.000);
    color += hub2 * float3(0.050, 0.270, 0.540);
    color += crossGlow * float3(0.030, 0.180, 0.380);
    color += saturate(nodes) * float3(0.120, 0.740, 1.000);
    color += saturate(links) * float3(0.025, 0.170, 0.300);
    color += sweep * float3(0.050, 0.270, 0.560);
    color += packet * float3(0.090, 0.560, 0.950);
    color += gauges * float3(0.020, 0.095, 0.115);
    color += cpuArc * float3(0.110, 0.790, 1.000);
    color += ramArc * float3(0.870, 0.690, 0.140);
    color += ssdArc * float3(0.240, 0.940, 0.720);

    return float4(saturate(color), 1.0);
}";

            IntPtr vsBlob = IntPtr.Zero;
            IntPtr psBlob = IntPtr.Zero;
            IntPtr errors = IntPtr.Zero;
            try
            {
                int hr = D3DCompile(hlsl, (UIntPtr)Encoding.ASCII.GetByteCount(hlsl), "nexus_hdr_probe.hlsl", IntPtr.Zero, IntPtr.Zero, "VSMain", "vs_4_0", D3DCOMPILE_ENABLE_STRICTNESS, 0, out vsBlob, out errors);
                if (hr < 0 || vsBlob == IntPtr.Zero) throw new Exception("D3DCompile VS hr=0x" + hr.ToString("x8", CultureInfo.InvariantCulture) + " " + BlobText(errors));

                hr = D3DCompile(hlsl, (UIntPtr)Encoding.ASCII.GetByteCount(hlsl), "nexus_hdr_probe.hlsl", IntPtr.Zero, IntPtr.Zero, "PSMain", "ps_4_0", D3DCOMPILE_ENABLE_STRICTNESS, 0, out psBlob, out errors);
                if (hr < 0 || psBlob == IntPtr.Zero) throw new Exception("D3DCompile PS hr=0x" + hr.ToString("x8", CultureInfo.InvariantCulture) + " " + BlobText(errors));

                IntPtr vsPtr;
                UIntPtr vsSize;
                GetBlobData(vsBlob, out vsPtr, out vsSize);
                hr = GetMethod<CreateVertexShaderDelegate>(device, 12)(device, vsPtr, vsSize, IntPtr.Zero, out vertexShader);
                if (hr < 0 || vertexShader == IntPtr.Zero) throw new Exception("CreateVertexShader hr=0x" + hr.ToString("x8", CultureInfo.InvariantCulture));

                IntPtr psPtr;
                UIntPtr psSize;
                GetBlobData(psBlob, out psPtr, out psSize);
                hr = GetMethod<CreatePixelShaderDelegate>(device, 15)(device, psPtr, psSize, IntPtr.Zero, out pixelShader);
                if (hr < 0 || pixelShader == IntPtr.Zero) throw new Exception("CreatePixelShader hr=0x" + hr.ToString("x8", CultureInfo.InvariantCulture));

                ShaderConstants initial = new ShaderConstants { Time = 0, Width = sceneWidth, Height = sceneHeight, Cpu = 0, Ram = 0, Ssd = 0, Pad0 = 0, Pad1 = 0 };
                int size = Marshal.SizeOf(typeof(ShaderConstants));
                IntPtr initialPtr = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(initial, initialPtr, false);
                    D3D11_BUFFER_DESC cbDesc = new D3D11_BUFFER_DESC();
                    cbDesc.ByteWidth = (uint)size;
                    cbDesc.Usage = 0; // D3D11_USAGE_DEFAULT
                    cbDesc.BindFlags = 0x4; // D3D11_BIND_CONSTANT_BUFFER
                    D3D11_SUBRESOURCE_DATA initData = new D3D11_SUBRESOURCE_DATA { pSysMem = initialPtr, SysMemPitch = 0, SysMemSlicePitch = 0 };
                    hr = GetMethod<CreateBufferDelegate>(device, 3)(device, ref cbDesc, ref initData, out constantBuffer);
                    if (hr < 0 || constantBuffer == IntPtr.Zero) throw new Exception("CreateBuffer constant hr=0x" + hr.ToString("x8", CultureInfo.InvariantCulture));
                }
                finally
                {
                    Marshal.FreeHGlobal(initialPtr);
                }
            }
            finally
            {
                Release(vsBlob);
                Release(psBlob);
                Release(errors);
            }
        }

        private static void GetBlobData(IntPtr blob, out IntPtr pointer, out UIntPtr size)
        {
            IntPtr vtbl = Marshal.ReadIntPtr(blob);
            IntPtr getBufferPointerFn = Marshal.ReadIntPtr(vtbl, 3 * IntPtr.Size);
            IntPtr getBufferSizeFn = Marshal.ReadIntPtr(vtbl, 4 * IntPtr.Size);
            var getPointer = (GetBufferPointerDelegate)Marshal.GetDelegateForFunctionPointer(getBufferPointerFn, typeof(GetBufferPointerDelegate));
            var getSize = (GetBufferSizeDelegate)Marshal.GetDelegateForFunctionPointer(getBufferSizeFn, typeof(GetBufferSizeDelegate));
            pointer = getPointer(blob);
            size = getSize(blob);
        }

        private static string BlobText(IntPtr blob)
        {
            if (blob == IntPtr.Zero) return "";
            try
            {
                IntPtr ptr;
                UIntPtr size;
                GetBlobData(blob, out ptr, out size);
                int len = (int)size;
                if (ptr == IntPtr.Zero || len <= 0) return "";
                byte[] bytes = new byte[len];
                Marshal.Copy(ptr, bytes, 0, len);
                return Encoding.ASCII.GetString(bytes).Replace("\r", " ").Replace("\n", " ");
            }
            catch
            {
                return "";
            }
        }

        private void UpdateTelemetry(bool force)
        {
            DateTime now = DateTime.Now;
            if (!force && (now - lastTelemetryUpdate).TotalMilliseconds < 500) return;
            lastTelemetryUpdate = now;

            try
            {
                FILETIME idle;
                FILETIME kernel;
                FILETIME user;
                if (GetSystemTimes(out idle, out kernel, out user))
                {
                    ulong idleTicks = FileTimeToUInt64(idle);
                    ulong totalTicks = FileTimeToUInt64(kernel) + FileTimeToUInt64(user);
                    if (previousCpuTotal != 0 && totalTicks > previousCpuTotal)
                    {
                        ulong totalDelta = totalTicks - previousCpuTotal;
                        ulong idleDelta = idleTicks > previousCpuIdle ? idleTicks - previousCpuIdle : 0;
                        telemetryCpu = Clamp01((float)(1.0 - Math.Min(1.0, idleDelta / (double)Math.Max(1UL, totalDelta))));
                    }
                    previousCpuIdle = idleTicks;
                    previousCpuTotal = totalTicks;
                }
            }
            catch {}

            try
            {
                MEMORYSTATUSEX memory = new MEMORYSTATUSEX();
                memory.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                if (GlobalMemoryStatusEx(ref memory))
                {
                    telemetryRam = Clamp01(memory.dwMemoryLoad / 100.0f);
                }
            }
            catch {}

            try
            {
                string root = Path.GetPathRoot(Environment.SystemDirectory);
                if (string.IsNullOrEmpty(root)) root = "C:\\";
                DriveInfo drive = new DriveInfo(root);
                if (drive.IsReady && drive.TotalSize > 0)
                {
                    telemetrySsd = Clamp01((float)(1.0 - (drive.AvailableFreeSpace / (double)drive.TotalSize)));
                }
            }
            catch {}

            telemetryUpdateCount++;
        }

        private static ulong FileTimeToUInt64(FILETIME time)
        {
            return ((ulong)(uint)time.dwHighDateTime << 32) | (uint)time.dwLowDateTime;
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0;
            if (value < 0) return 0;
            if (value > 1) return 1;
            return value;
        }

        private void RenderFrame()
        {
            if (context == IntPtr.Zero || renderTargetView == IntPtr.Zero || vertexShader == IntPtr.Zero || pixelShader == IntPtr.Zero || constantBuffer == IntPtr.Zero) return;

            double t = (DateTime.Now - started).TotalSeconds;
            UpdateTelemetry(false);

            ShaderConstants constants = new ShaderConstants
            {
                Time = (float)t,
                Width = sceneWidth,
                Height = sceneHeight,
                Cpu = telemetryCpu,
                Ram = telemetryRam,
                Ssd = telemetrySsd,
                Pad0 = 0,
                Pad1 = 0
            };
            int size = Marshal.SizeOf(typeof(ShaderConstants));
            IntPtr data = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(constants, data, false);
                GetMethod<UpdateSubresourceDelegate>(context, 48)(context, constantBuffer, 0, IntPtr.Zero, data, (uint)size, 0);
            }
            finally
            {
                Marshal.FreeHGlobal(data);
            }

            D3D11_VIEWPORT[] vp = new D3D11_VIEWPORT[]
            {
                new D3D11_VIEWPORT { TopLeftX = 0, TopLeftY = 0, Width = sceneWidth, Height = sceneHeight, MinDepth = 0, MaxDepth = 1 }
            };

            IntPtr cbArray = Marshal.AllocHGlobal(IntPtr.Size);
            try
            {
                Marshal.WriteIntPtr(cbArray, constantBuffer);
                GetMethod<OMSetRenderTargetsDelegate>(context, 33)(context, 1, rtvArray, IntPtr.Zero);
                GetMethod<RSSetViewportsDelegate>(context, 44)(context, 1, vp);
                GetMethod<IASetPrimitiveTopologyDelegate>(context, 24)(context, 4); // D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST
                GetMethod<VSSetShaderDelegate>(context, 11)(context, vertexShader, IntPtr.Zero, 0);
                GetMethod<PSSetShaderDelegate>(context, 9)(context, pixelShader, IntPtr.Zero, 0);
                GetMethod<PSSetConstantBuffersDelegate>(context, 16)(context, 0, 1, cbArray);
                GetMethod<DrawDelegate>(context, 13)(context, 3, 0);
                renderCount++;
            }
            finally
            {
                Marshal.FreeHGlobal(cbArray);
            }
        }

        private string BuildResultJson()
        {
            if (initOk)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "{{\"ok\":true,\"adapter\":{0},\"featureLevel\":\"0x{1:x}\",\"format\":\"R10G10B10A2_UNORM\",\"colorSpace\":\"DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020\",\"checkColorSpaceSupport\":{2},\"presentSupport\":{3},\"setColorSpace1\":{4},\"renderTargetView\":{5},\"gpuShaderScene\":{6},\"renderPass\":\"GPU fullscreen HLSL Nexus HDR scene with lightweight Win32 telemetry gauges\",\"renderWidth\":{7},\"renderHeight\":{8},\"desktopAttached\":true,\"seconds\":{9},\"presentCount\":{10},\"renderCount\":{11},\"skippedFrameCount\":{12},\"suspendCount\":{13},\"resumeCount\":{14},\"runtimeSuspended\":{15},\"fullscreenReason\":{16},\"fullscreenEvents\":{17},\"fullscreenForegroundChecks\":{18},\"fullscreenFallbackScans\":{19},\"telemetry\":{{\"cpu\":{20:0.0000},\"ram\":{21:0.0000},\"ssd\":{22:0.0000},\"updates\":{23}}}}}",
                    Json(resultAdapterName),
                    resultFeatureLevel,
                    resultColorSpaceSupport,
                    resultPresentSupport ? "true" : "false",
                    resultSetColorSpaceOk ? "true" : "false",
                    resultRenderTargetViewOk ? "true" : "false",
                    resultGpuShaderOk ? "true" : "false",
                    sceneWidth,
                    sceneHeight,
                    seconds,
                    presentCount,
                    renderCount,
                    skippedFrameCount,
                    suspendCount,
                    resumeCount,
                    runtimeSuspended ? "true" : "false",
                    Json(fullscreenReason),
                    fullscreenEventCount,
                    fullscreenForegroundCheckCount,
                    fullscreenFallbackScanCount,
                    telemetryCpu,
                    telemetryRam,
                    telemetrySsd,
                    telemetryUpdateCount);
            }

            return string.Format(CultureInfo.InvariantCulture,
                "{{\"ok\":false,\"error\":{0},\"adapter\":{1},\"featureLevel\":\"0x{2:x}\",\"checkColorSpaceSupport\":{3},\"presentSupport\":{4},\"setColorSpace1\":{5},\"renderTargetView\":{6},\"gpuShaderScene\":{7},\"presentCount\":{8},\"renderCount\":{9},\"skippedFrameCount\":{10},\"suspendCount\":{11},\"resumeCount\":{12},\"runtimeSuspended\":{13},\"fullscreenReason\":{14},\"fullscreenEvents\":{15},\"fullscreenForegroundChecks\":{16},\"fullscreenFallbackScans\":{17},\"telemetry\":{{\"cpu\":{18:0.0000},\"ram\":{19:0.0000},\"ssd\":{20:0.0000},\"updates\":{21}}}}}",
                Json(resultError),
                Json(resultAdapterName),
                resultFeatureLevel,
                resultColorSpaceSupport,
                resultPresentSupport ? "true" : "false",
                resultSetColorSpaceOk ? "true" : "false",
                resultRenderTargetViewOk ? "true" : "false",
                resultGpuShaderOk ? "true" : "false",
                presentCount,
                renderCount,
                skippedFrameCount,
                suspendCount,
                resumeCount,
                runtimeSuspended ? "true" : "false",
                Json(fullscreenReason),
                fullscreenEventCount,
                fullscreenForegroundCheckCount,
                fullscreenFallbackScanCount,
                telemetryCpu,
                telemetryRam,
                telemetrySsd,
                telemetryUpdateCount);
        }

        private static IntPtr SelectAdapter(IntPtr factory, out string adapterName)
        {
            adapterName = "unknown";
            EnumAdapters1Delegate enumAdapters1 = GetMethod<EnumAdapters1Delegate>(factory, 12);
            IntPtr firstAdapter = IntPtr.Zero;
            string firstName = "unknown";

            for (uint i = 0; ; i++)
            {
                IntPtr candidate;
                int hr = enumAdapters1(factory, i, out candidate);
                if (hr == DXGI_ERROR_NOT_FOUND) break;
                if (hr < 0 || candidate == IntPtr.Zero) continue;

                string name = "unknown";
                try
                {
                    DXGI_ADAPTER_DESC1 desc;
                    if (GetMethod<AdapterGetDesc1Delegate>(candidate, 10)(candidate, out desc) >= 0)
                    {
                        name = desc.Description.TrimEnd('\0');
                    }
                }
                catch {}

                if (firstAdapter == IntPtr.Zero)
                {
                    firstAdapter = candidate;
                    firstName = name;
                }

                if (name.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("UHD", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    adapterName = name;
                    return candidate;
                }
            }

            adapterName = firstName;
            return firstAdapter;
        }

        protected override void OnClosed(EventArgs e)
        {
            ResultJson = BuildResultJson();
            if (frameTimer != null) frameTimer.Dispose();
            if (fullscreenTimer != null) fullscreenTimer.Dispose();
            if (fullscreenFallbackTimer != null) fullscreenFallbackTimer.Dispose();
            if (fullscreenEventHook != IntPtr.Zero)
            {
                UnhookWinEvent(fullscreenEventHook);
                fullscreenEventHook = IntPtr.Zero;
            }
            if (rtvArray != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(rtvArray);
                rtvArray = IntPtr.Zero;
            }
            Release(constantBuffer);
            Release(pixelShader);
            Release(vertexShader);
            Release(renderTargetView);
            Release(backBufferTexture);
            Release(swapChain3);
            Release(swapChain);
            Release(context);
            Release(device);
            Release(adapter);
            Release(factory);
            base.OnClosed(e);
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


