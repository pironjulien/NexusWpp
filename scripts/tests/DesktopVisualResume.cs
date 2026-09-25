// Measures the physical desktop composition, not WebView logs or a rendered DOM.
// All timestamps use one Stopwatch. No visual pass/fail threshold is imposed.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class DesktopVisualResume
{
    delegate bool EnumProc(IntPtr window, IntPtr unused);
    [StructLayout(LayoutKind.Sequential)] struct PointNative { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct RectNative { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct Placement
    {
        public int Length, Flags, ShowCmd;
        public PointNative Minimum, Maximum;
        public RectNative Normal;
    }
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr unused);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr window, ref Placement placement);
    [DllImport("user32.dll")] static extern bool SetWindowPlacement(IntPtr window, ref Placement placement);
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder value, int size);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("dwmapi.dll")] static extern int DwmFlush();
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(PointNative point, uint flags);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int kind, out uint x, out uint y);

    static readonly Stopwatch Clock = new Stopwatch();
    static readonly Color CoverColor = Color.FromArgb(167, 43, 83);
    static readonly List<Form> Covers = new List<Form>();
    static readonly List<SavedWindow> Windows = new List<SavedWindow>();
    static readonly List<ScenarioResult> Scenarios = new List<ScenarioResult>();
    static readonly List<string> Restoration = new List<string>();
    static readonly Dictionary<string, string> Options = new Dictionary<string, string>();
    static string output, startedAt, error;
    static IntPtr foreground;
    static Screen[] screens;
    static int appPid;
    static bool restored;
    static double Now { get { return Clock.Elapsed.TotalMilliseconds; } }
    static int Option(string name) { return int.Parse(Options[name], CultureInfo.InvariantCulture); }
    static string Number(double number) { return number.ToString("F3", CultureInfo.InvariantCulture); }

    sealed class SavedWindow { internal IntPtr Handle; internal uint Pid; internal Placement Placement; internal bool Topmost; }
    sealed class CoverForm : Form
    {
        internal void MaximizeOn(Rectangle area) { MaximizedBounds = area; WindowState = FormWindowState.Maximized; }
    }
    public sealed class Area
    {
        public int X, Y, Width, Height;
        public Area(Rectangle r) { X = r.X; Y = r.Y; Width = r.Width; Height = r.Height; }
    }
    public sealed class Anchor
    {
        public int X, Y, OtherX, OtherY, Region, ReferenceRgb, OtherReferenceRgb, Contrast;
    }
    public sealed class RegionMetric
    {
        public int Region, AnchorPairs, ExactPairs;
        public double ExactPairFraction, MeanAbsoluteChannelError, MeanDirectionalContrastRatio;
    }
    public sealed class FrameMetric
    {
        public string Phase, ThumbnailPath, NativeImagePath;
        public int Index, AnchorPairs, ExactPairs, CoverGridSamples, ExactOpaqueCoverGridSamples;
        public double CaptureStartMilliseconds, CaptureEndMilliseconds, CaptureDurationMilliseconds;
        public double DwmFlushStartMilliseconds, DwmFlushEndMilliseconds, RelativeToRemovalMilliseconds;
        public double ExactPairFraction, MeanAbsoluteChannelError;
        public int DwmFlushHResult;
        public List<RegionMetric> Regions = new List<RegionMetric>();
    }
    public sealed class ScreenResult
    {
        public string DeviceName, ReferenceImagePath, AnchorImagePath;
        public Area Bounds, WorkingArea;
        public uint DpiX, DpiY;
        public int CalibrationFrames, AnchorPairCount;
        public double CalibrationDurationMilliseconds;
        public double? FirstUncoverCaptureEndMilliseconds, FirstExactReferenceEndMilliseconds;
        public double? FirstOpaqueCoverAbsentEndMilliseconds;
        public double WorstExactPairFractionAfterRemoval, CaptureIntervalMedianMilliseconds, CaptureIntervalMaximumMilliseconds;
        public List<Anchor> Anchors = new List<Anchor>();
        public List<FrameMetric> Frames = new List<FrameMetric>();
        public List<string> Filmstrips = new List<string>();
    }
    public sealed class ScenarioResult
    {
        public string Name;
        public double CoverShowStartMilliseconds, CoverShowEndMilliseconds, RemovalStartMilliseconds, RemovalCompleteMilliseconds;
        public List<Area> ActualCoverBounds = new List<Area>();
        public double CoverOpacity;
        public List<ScreenResult> Screens = new List<ScreenResult>();
    }
    sealed class ImageFrame : IDisposable
    {
        internal FrameMetric Metric;
        internal Bitmap Thumbnail, Native;
        internal bool KeepNative;
        public void Dispose() { if (Thumbnail != null) Thumbnail.Dispose(); if (Native != null) Native.Dispose(); }
    }
    sealed class ScreenProbe : IDisposable
    {
        internal Screen Screen;
        internal ScreenResult Result;
        internal Bitmap Surface, Reference;
        internal readonly List<ImageFrame> Images = new List<ImageFrame>();
        internal int[] Pixels;
        internal bool[] Stable;
        internal int ResumeFrames;
        internal double Worst = double.PositiveInfinity;
        internal ImageFrame WorstFrame;
        internal bool ExactNativeSaved;
        public void Dispose()
        {
            foreach (ImageFrame image in Images) image.Dispose();
            if (Reference != null) Reference.Dispose();
            if (Surface != null) Surface.Dispose();
        }
    }

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            for (int i = 0; i < args.Length; i += 2) Options.Add(args[i], args[i + 1]);
            output = Options["--out"];
            Directory.CreateDirectory(output);
            appPid = Option("--app-pid");
            using (Process process = Process.GetProcessById(appPid))
                if (process.ProcessName.ToLowerInvariant() != "nexuswpp") throw new InvalidOperationException("The application process changed.");
            try { if (!SetProcessDpiAwarenessContext(new IntPtr(-4))) SetProcessDPIAware(); }
            catch (EntryPointNotFoundException) { SetProcessDPIAware(); }
            Application.EnableVisualStyles();
            screens = Screen.AllScreens;
            startedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            Clock.Start();
            foreground = GetForegroundWindow();
            SaveAndMinimizeWindows();
            Pump(3000); // Initial reveal is not a measured transition; allow calibration on an established scene.
            string[] names = Options["--scenario"] == "all"
                ? new[] { "maximized", "tiled", "fullscreen", "partial", "transparent", "rapid" }
                : new[] { Options["--scenario"] };
            foreach (string name in names)
            {
                int repeats = name == "rapid" ? Option("--rapid-cycles") : 1;
                List<ScreenProbe> probes = Calibrate(name);
                try
                {
                    for (int cycle = 0; cycle < repeats; cycle++)
                    {
                        // Rapid cycles have no image encoding, recalibration or disk I/O between transitions.
                        RunScenario(name, cycle, probes);
                    }
                    foreach (ScreenProbe probe in probes) SaveImages(probe);
                    WriteResult();
                }
                finally { foreach (ScreenProbe probe in probes) probe.Dispose(); CloseCovers(); }
            }
            return 0;
        }
        catch (Exception exception) { error = exception.ToString(); return 1; }
        finally
        {
            CloseCovers();
            RestoreWindows();
            if (output != null) { try { WriteResult(); WriteIndex(); } catch (Exception exception) { File.WriteAllText(Path.Combine(output, "write-error.txt"), exception.ToString()); } }
        }
    }

    static void SaveAndMinimizeWindows()
    {
        uint ownPid = (uint)Process.GetCurrentProcess().Id;
        EnumWindows(delegate(IntPtr window, IntPtr unused)
        {
            uint pid; GetWindowThreadProcessId(window, out pid);
            if (pid == ownPid || pid == appPid || !IsWindowVisible(window) || IsIconic(window)) return true;
            StringBuilder className = new StringBuilder(256); GetClassName(window, className, className.Capacity);
            string cls = className.ToString();
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd" || cls == "Button") return true;
            int cloaked;
            if (DwmGetWindowAttribute(window, 14, out cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            Placement placement = new Placement { Length = Marshal.SizeOf(typeof(Placement)) };
            if (!GetWindowPlacement(window, ref placement)) return true;
            Windows.Add(new SavedWindow { Handle = window, Pid = pid, Placement = placement, Topmost = (GetWindowLongPtr(window, -20).ToInt64() & 8) != 0 });
            return true;
        }, IntPtr.Zero);
        foreach (SavedWindow saved in Windows) ShowWindowAsync(saved.Handle, 6);
    }

    static void RestoreWindows()
    {
        if (restored) return;
        restored = true;
        // Restore placement and z-order from bottom to top without activating every application.
        for (int i = Windows.Count - 1; i >= 0; i--)
        {
            SavedWindow saved = Windows[i]; uint pid;
            GetWindowThreadProcessId(saved.Handle, out pid);
            if (!IsWindow(saved.Handle) || pid != saved.Pid) { Restoration.Add("Window closed or replaced: " + saved.Handle); continue; }
            Placement placement = saved.Placement;
            bool placed = SetWindowPlacement(saved.Handle, ref placement);
            bool ordered = SetWindowPos(saved.Handle, saved.Topmost ? new IntPtr(-1) : IntPtr.Zero, 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
            Restoration.Add("Window " + saved.Handle + ": placement=" + placed + ", zOrder=" + ordered);
        }
        if (foreground != IntPtr.Zero && IsWindow(foreground)) Restoration.Add("Foreground restored=" + SetForegroundWindow(foreground));
    }

    static void Pump(int milliseconds)
    {
        double end = Now + milliseconds;
        while (Now < end) { Application.DoEvents(); Thread.Sleep(1); }
    }

    static void Capture(ScreenProbe probe)
    {
        using (Graphics graphics = Graphics.FromImage(probe.Surface))
            graphics.CopyFromScreen(probe.Screen.Bounds.Location, Point.Empty, probe.Screen.Bounds.Size, CopyPixelOperation.SourceCopy);
    }

    static int[] ReadPixels(Bitmap bitmap)
    {
        Rectangle rectangle = new Rectangle(Point.Empty, bitmap.Size);
        BitmapData data = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            int[] values = new int[bitmap.Width * bitmap.Height];
            for (int y = 0; y < bitmap.Height; y++) Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), values, y * bitmap.Width, bitmap.Width);
            for (int i = 0; i < values.Length; i++) values[i] &= 0xffffff;
            return values;
        }
        finally { bitmap.UnlockBits(data); }
    }

    static List<ScreenProbe> Calibrate(string name)
    {
        List<ScreenProbe> probes = new List<ScreenProbe>();
        foreach (Screen screen in screens)
        {
            ScreenResult result = new ScreenResult { DeviceName = screen.DeviceName, Bounds = new Area(screen.Bounds), WorkingArea = new Area(screen.WorkingArea) };
            uint dx, dy;
            if (GetDpiForMonitor(MonitorFromPoint(new PointNative { X = screen.Bounds.X, Y = screen.Bounds.Y }, 2), 0, out dx, out dy) == 0) { result.DpiX = dx; result.DpiY = dy; }
            ScreenProbe probe = new ScreenProbe { Screen = screen, Result = result, Surface = new Bitmap(screen.Bounds.Width, screen.Bounds.Height, PixelFormat.Format32bppRgb) };
            probes.Add(probe);
        }
        double start = Now;
        int count = 0;
        do
        {
            DwmFlush();
            foreach (ScreenProbe probe in probes)
            {
                Capture(probe);
                int[] pixels = ReadPixels(probe.Surface);
                if (probe.Pixels == null)
                {
                    probe.Pixels = pixels; probe.Stable = Enumerable.Repeat(true, pixels.Length).ToArray();
                    probe.Reference = (Bitmap)probe.Surface.Clone();
                }
                else for (int i = 0; i < pixels.Length; i++) if (pixels[i] != probe.Pixels[i]) probe.Stable[i] = false;
            }
            count++;
            Pump(100);
        } while (Now - start < Option("--baseline-ms"));
        for (int i = 0; i < probes.Count; i++)
        {
            ScreenProbe probe = probes[i];
            probe.Result.CalibrationFrames = count;
            probe.Result.CalibrationDurationMilliseconds = Now - start;
            SelectAnchors(probe);
            string prefix = name + "-screen" + i;
            probe.Result.ReferenceImagePath = prefix + "-reference.png";
            probe.Result.AnchorImagePath = prefix + "-anchors.png";
            probe.Reference.Save(Path.Combine(output, probe.Result.ReferenceImagePath), ImageFormat.Png);
            using (Bitmap annotated = (Bitmap)probe.Reference.Clone())
            using (Graphics graphics = Graphics.FromImage(annotated))
            using (Pen pen = new Pen(Color.Lime, 1))
            {
                foreach (Anchor anchor in probe.Result.Anchors) graphics.DrawEllipse(pen, anchor.X - 3, anchor.Y - 3, 6, 6);
                annotated.Save(Path.Combine(output, probe.Result.AnchorImagePath), ImageFormat.Png);
            }
            // These buffers serve calibration only; capture measurements read selected physical pixels directly.
            probe.Pixels = null; probe.Stable = null;
        }
        return probes;
    }

    static int ErrorRgb(int a, int b)
    {
        return Math.Abs((a & 255) - (b & 255)) + Math.Abs(((a >> 8) & 255) - ((b >> 8) & 255)) + Math.Abs(((a >> 16) & 255) - ((b >> 16) & 255));
    }

    static void SelectAnchors(ScreenProbe probe)
    {
        int width = probe.Surface.Width, height = probe.Surface.Height;
        // Spatial sampling over the real desktop, excluding taskbars and the outer icon strip.
        // Strong stable edges are learned from pixels, not guessed from DOM positions.
        Rectangle work = probe.Screen.WorkingArea; work.Offset(-probe.Screen.Bounds.X, -probe.Screen.Bounds.Y);
        Rectangle region = new Rectangle(work.X + work.Width / 20, work.Y + work.Height / 12, work.Width * 9 / 10, work.Height * 10 / 12);
        for (int row = 0; row < 4; row++) for (int column = 0; column < 6; column++)
        {
            List<Anchor> candidates = new List<Anchor>();
            int left = region.X + region.Width * column / 6, right = region.X + region.Width * (column + 1) / 6;
            int top = region.Y + region.Height * row / 4, bottom = region.Y + region.Height * (row + 1) / 4;
            for (int y = Math.Max(1, top); y < Math.Min(height - 2, bottom); y += 2)
                for (int x = Math.Max(1, left); x < Math.Min(width - 2, right); x += 2)
                {
                    int at = y * width + x;
                    if (!probe.Stable[at]) continue;
                    int other = at + 1;
                    if (ErrorRgb(probe.Pixels[at], probe.Pixels[at + width]) > ErrorRgb(probe.Pixels[at], probe.Pixels[other])) other = at + width;
                    if (!probe.Stable[other]) continue;
                    int contrast = ErrorRgb(probe.Pixels[at], probe.Pixels[other]);
                    if (contrast == 0 || (candidates.Count == 32 && contrast <= candidates[candidates.Count - 1].Contrast)) continue;
                    if (candidates.Any(a => Math.Abs(a.X - x) <= 3 && Math.Abs(a.Y - y) <= 3)) continue;
                    candidates.Add(new Anchor { X = x, Y = y, OtherX = other % width, OtherY = other / width, Region = row * 6 + column, ReferenceRgb = probe.Pixels[at], OtherReferenceRgb = probe.Pixels[other], Contrast = contrast });
                    candidates.Sort((a, b) => b.Contrast.CompareTo(a.Contrast));
                    if (candidates.Count > 32) candidates.RemoveAt(candidates.Count - 1);
                }
            probe.Result.Anchors.AddRange(candidates);
        }
        probe.Result.AnchorPairCount = probe.Result.Anchors.Count;
    }

    static void ShowCovers(string name, ScenarioResult result)
    {
        foreach (Screen screen in screens)
        {
            Rectangle area = name == "fullscreen" ? screen.Bounds : screen.WorkingArea;
            int parts = name == "tiled" ? 2 : 1;
            for (int part = 0; part < parts; part++)
            {
                Rectangle bounds = Rectangle.FromLTRB(area.Left + area.Width * part / parts, area.Top, area.Left + area.Width * (part + 1) / parts, area.Bottom);
                if (name == "partial") bounds.Width /= 2;
                CoverForm form = new CoverForm { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, ShowInTaskbar = false, TopMost = true, Bounds = bounds, BackColor = CoverColor, Text = "NexusWpp visual measurement" };
                if (name == "transparent") form.Opacity = 0.5;
                Covers.Add(form);
                form.Show();
                if (name == "maximized") form.MaximizeOn(area);
                result.ActualCoverBounds.Add(new Area(form.Bounds));
            }
        }
        Application.DoEvents();
    }

    static void CloseCovers()
    {
        foreach (Form form in Covers) { try { form.Hide(); form.Close(); form.Dispose(); } catch (ObjectDisposedException) { } }
        Covers.Clear();
    }

    static void RunScenario(string name, int cycle, List<ScreenProbe> probes)
    {
        EnsureScreensUnchanged();
        ScenarioResult result = new ScenarioResult { Name = name == "rapid" ? name + "-" + (cycle + 1) : name, CoverOpacity = name == "transparent" ? 0.5 : 1 };
        foreach (ScreenProbe probe in probes)
        {
            if (cycle > 0)
            {
                ScreenResult previous = probe.Result;
                probe.Result = new ScreenResult { DeviceName = previous.DeviceName, Bounds = previous.Bounds, WorkingArea = previous.WorkingArea, DpiX = previous.DpiX, DpiY = previous.DpiY, CalibrationFrames = previous.CalibrationFrames, CalibrationDurationMilliseconds = previous.CalibrationDurationMilliseconds, Anchors = previous.Anchors, AnchorPairCount = previous.AnchorPairCount, ReferenceImagePath = previous.ReferenceImagePath, AnchorImagePath = previous.AnchorImagePath };
            }
            probe.ResumeFrames = 0; probe.Worst = double.PositiveInfinity; probe.WorstFrame = null; probe.ExactNativeSaved = false;
            result.Screens.Add(probe.Result);
        }
        Scenarios.Add(result);
        // The last actual scene before coverage is retained as a separate native frame.
        SampleFor(result, probes, "before-cover", 1, 1);
        result.CoverShowStartMilliseconds = Now;
        ShowCovers(name == "rapid" ? "tiled" : name, result);
        result.CoverShowEndMilliseconds = Now;
        SampleFor(result, probes, "covered", name == "rapid" ? 250 : Option("--cover-ms"), 200);
        result.RemovalStartMilliseconds = Now;
        foreach (Form form in Covers) form.Hide();
        result.RemovalCompleteMilliseconds = Now;
        SampleFor(result, probes, "uncovered", name == "rapid" ? 250 : Option("--resume-ms"), Option("--sample-ms"));
        CloseCovers();
        foreach (ScreenProbe probe in probes) Summarize(result, probe.Result);
    }

    static void EnsureScreensUnchanged()
    {
        Screen[] current = Screen.AllScreens;
        if (current.Length != screens.Length || current.Where((s, i) => s.DeviceName != screens[i].DeviceName || s.Bounds != screens[i].Bounds).Any())
            throw new InvalidOperationException("Monitor topology changed; this run cannot preserve one spatial reference.");
    }

    static void SampleFor(ScenarioResult scenario, List<ScreenProbe> probes, string phase, int duration, int interval)
    {
        double start = Now, next = start;
        do
        {
            Application.DoEvents();
            double flushStart = Now; int flushResult = DwmFlush(); double flushEnd = Now;
            foreach (ScreenProbe probe in probes)
            {
                FrameMetric metric = new FrameMetric { Index = probe.Images.Count, Phase = phase, DwmFlushStartMilliseconds = flushStart, DwmFlushEndMilliseconds = flushEnd, DwmFlushHResult = flushResult, CaptureStartMilliseconds = Now };
                Capture(probe);
                metric.CaptureEndMilliseconds = Now;
                metric.CaptureDurationMilliseconds = metric.CaptureEndMilliseconds - metric.CaptureStartMilliseconds;
                Measure(probe, metric);
                bool uncovered = phase == "uncovered";
                ImageFrame image = new ImageFrame { Metric = metric, Thumbnail = Thumbnail(probe.Surface, 640) };
                if (phase == "before-cover") { image.Native = (Bitmap)probe.Surface.Clone(); image.KeepNative = true; }
                if (uncovered)
                {
                    // Preserve native pixels of the first four frames, the first exact match and the largest observed mismatch.
                    if (probe.ResumeFrames < (scenario.Name.StartsWith("rapid-") ? 2 : 4) || (!probe.ExactNativeSaved && metric.AnchorPairs > 0 && metric.ExactPairs == metric.AnchorPairs)) { image.Native = (Bitmap)probe.Surface.Clone(); image.KeepNative = true; }
                    if (metric.AnchorPairs > 0 && metric.ExactPairs == metric.AnchorPairs) probe.ExactNativeSaved = true;
                    if (metric.ExactPairFraction < probe.Worst)
                    {
                        if (probe.WorstFrame != null && !probe.WorstFrame.KeepNative && probe.WorstFrame.Native != null) { probe.WorstFrame.Native.Dispose(); probe.WorstFrame.Native = null; }
                        if (image.Native == null) image.Native = (Bitmap)probe.Surface.Clone();
                        probe.WorstFrame = image; probe.Worst = metric.ExactPairFraction;
                    }
                    probe.ResumeFrames++;
                }
                probe.Images.Add(image); probe.Result.Frames.Add(metric);
            }
            next += interval;
            while (Now < next) { Application.DoEvents(); Thread.Sleep(1); }
        } while (Now - start < duration);
        if (phase == "uncovered" && !scenario.Name.StartsWith("rapid-"))
            foreach (ScreenProbe probe in probes)
            {
                ImageFrame last = probe.Images[probe.Images.Count - 1];
                if (last.Native == null) last.Native = (Bitmap)probe.Surface.Clone();
                last.KeepNative = true;
            }
    }

    static Bitmap Thumbnail(Bitmap native, int width)
    {
        int height = Math.Max(1, native.Height * width / native.Width);
        Bitmap thumbnail = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (Graphics graphics = Graphics.FromImage(thumbnail)) { graphics.InterpolationMode = InterpolationMode.Bilinear; graphics.DrawImage(native, new Rectangle(0, 0, width, height)); }
        return thumbnail;
    }

    static unsafe int Pixel(BitmapData data, int x, int y) { return ((int*)((byte*)data.Scan0 + y * data.Stride))[x] & 0xffffff; }
    static unsafe void Measure(ScreenProbe probe, FrameMetric frame)
    {
        BitmapData data = probe.Surface.LockBits(new Rectangle(Point.Empty, probe.Surface.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            foreach (IGrouping<int, Anchor> group in probe.Result.Anchors.GroupBy(a => a.Region))
            {
                RegionMetric metric = new RegionMetric { Region = group.Key };
                foreach (Anchor anchor in group)
                {
                    int pixel = Pixel(data, anchor.X, anchor.Y), other = Pixel(data, anchor.OtherX, anchor.OtherY);
                    metric.AnchorPairs++;
                    if (pixel == anchor.ReferenceRgb && other == anchor.OtherReferenceRgb) metric.ExactPairs++;
                    metric.MeanAbsoluteChannelError += (ErrorRgb(pixel, anchor.ReferenceRgb) + ErrorRgb(other, anchor.OtherReferenceRgb)) / 6.0;
                    double dot = 0, norm = 0;
                    for (int channel = 0; channel < 3; channel++)
                    {
                        int actual = ((pixel >> (channel * 8)) & 255) - ((other >> (channel * 8)) & 255);
                        int expected = ((anchor.ReferenceRgb >> (channel * 8)) & 255) - ((anchor.OtherReferenceRgb >> (channel * 8)) & 255);
                        dot += actual * expected; norm += expected * expected;
                    }
                    metric.MeanDirectionalContrastRatio += norm == 0 ? 0 : dot / norm;
                }
                frame.AnchorPairs += metric.AnchorPairs; frame.ExactPairs += metric.ExactPairs;
                frame.MeanAbsoluteChannelError += metric.MeanAbsoluteChannelError;
                metric.ExactPairFraction = (double)metric.ExactPairs / metric.AnchorPairs;
                metric.MeanAbsoluteChannelError /= metric.AnchorPairs; metric.MeanDirectionalContrastRatio /= metric.AnchorPairs;
                frame.Regions.Add(metric);
            }
            frame.ExactPairFraction = frame.AnchorPairs == 0 ? 0 : (double)frame.ExactPairs / frame.AnchorPairs;
            if (frame.AnchorPairs != 0) frame.MeanAbsoluteChannelError /= frame.AnchorPairs;
            Rectangle work = probe.Screen.WorkingArea; work.Offset(-probe.Screen.Bounds.X, -probe.Screen.Bounds.Y);
            int coverRgb = CoverColor.ToArgb() & 0xffffff;
            for (int y = 0; y < 18; y++) for (int x = 0; x < 32; x++)
            {
                frame.CoverGridSamples++;
                if (Pixel(data, work.Left + work.Width * (2 * x + 1) / 64, work.Top + work.Height * (2 * y + 1) / 36) == coverRgb) frame.ExactOpaqueCoverGridSamples++;
            }
        }
        finally { probe.Surface.UnlockBits(data); }
    }

    static void Summarize(ScenarioResult scenario, ScreenResult result)
    {
        foreach (FrameMetric frame in result.Frames) frame.RelativeToRemovalMilliseconds = frame.CaptureStartMilliseconds - scenario.RemovalCompleteMilliseconds;
        List<FrameMetric> frames = result.Frames.Where(f => f.Phase == "uncovered").ToList();
        if (frames.Count == 0) return;
        result.FirstUncoverCaptureEndMilliseconds = frames[0].CaptureEndMilliseconds - scenario.RemovalCompleteMilliseconds;
        FrameMetric exact = frames.FirstOrDefault(f => f.AnchorPairs > 0 && f.ExactPairs == f.AnchorPairs && f.ExactOpaqueCoverGridSamples == 0);
        if (exact != null) result.FirstExactReferenceEndMilliseconds = exact.CaptureEndMilliseconds - scenario.RemovalCompleteMilliseconds;
        if (result.Frames.Any(f => f.Phase == "covered" && f.ExactOpaqueCoverGridSamples > 0))
        {
            FrameMetric absent = frames.FirstOrDefault(f => f.ExactOpaqueCoverGridSamples == 0);
            if (absent != null) result.FirstOpaqueCoverAbsentEndMilliseconds = absent.CaptureEndMilliseconds - scenario.RemovalCompleteMilliseconds;
        }
        result.WorstExactPairFractionAfterRemoval = frames.Min(f => f.ExactPairFraction);
        List<double> intervals = frames.Skip(1).Select((f, i) => f.CaptureStartMilliseconds - frames[i].CaptureStartMilliseconds).OrderBy(v => v).ToList();
        if (intervals.Count > 0) { result.CaptureIntervalMedianMilliseconds = intervals[intervals.Count / 2]; result.CaptureIntervalMaximumMilliseconds = intervals[intervals.Count - 1]; }
    }

    static void SaveImages(ScreenProbe probe)
    {
        // Flush after measured transitions so PNG/JPEG encoding cannot delay the captured resume.
        string directory = "screen" + Array.IndexOf(screens, probe.Screen) + "-" + probe.Result.ReferenceImagePath.Split('-')[0];
        Directory.CreateDirectory(Path.Combine(output, directory));
        for (int i = 0; i < probe.Images.Count; i++)
        {
            ImageFrame image = probe.Images[i];
            string stem = directory + "/frame-" + i.ToString("D5", CultureInfo.InvariantCulture) + "-" + Number(image.Metric.CaptureStartMilliseconds) + "ms";
            image.Metric.ThumbnailPath = stem + ".jpg";
            image.Thumbnail.Save(Path.Combine(output, image.Metric.ThumbnailPath), ImageFormat.Jpeg);
            if (image.Native != null) { image.Metric.NativeImagePath = stem + "-native.png"; image.Native.Save(Path.Combine(output, image.Metric.NativeImagePath), ImageFormat.Png); }
        }
        int pageSize = 24;
        using (Font font = new Font("Segoe UI", 10))
        for (int offset = 0; offset < probe.Images.Count; offset += pageSize)
        {
            int count = Math.Min(pageSize, probe.Images.Count - offset), thumbHeight = probe.Images[0].Thumbnail.Height;
            using (Bitmap strip = new Bitmap(640 * 3, (thumbHeight + 26) * ((count + 2) / 3)))
            using (Graphics graphics = Graphics.FromImage(strip))
            {
                graphics.Clear(Color.FromArgb(20, 20, 20));
                for (int item = 0; item < count; item++)
                {
                    ImageFrame image = probe.Images[offset + item]; int x = item % 3 * 640, y = item / 3 * (thumbHeight + 26);
                    graphics.DrawImageUnscaled(image.Thumbnail, x, y);
                    graphics.DrawString(image.Metric.Phase + "  " + Number(image.Metric.RelativeToRemovalMilliseconds) + " ms  exact=" + image.Metric.ExactPairFraction.ToString("P1", CultureInfo.InvariantCulture), font, Brushes.White, x, y + thumbHeight);
                }
                string path = directory + "/filmstrip-" + (offset / pageSize).ToString("D3", CultureInfo.InvariantCulture) + ".jpg";
                strip.Save(Path.Combine(output, path), ImageFormat.Jpeg);
                foreach (ScenarioResult scenario in Scenarios)
                    foreach (ScreenResult result in scenario.Screens)
                        if (result.DeviceName == probe.Screen.DeviceName && result.ReferenceImagePath == probe.Result.ReferenceImagePath) result.Filmstrips.Add(path);
            }
        }
    }

    static void WriteResult()
    {
        JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 };
        object payload = new
        {
            Label = Options.ContainsKey("--label") ? Options["--label"] : null,
            StartedAtUtc = startedAt, StopwatchFrequency = Stopwatch.Frequency,
            CaptureMethod = "Physical screen CopyFromScreen(SourceCopy), per-monitor DPI aware v2, DwmFlush before captures",
            Parameters = Options, Error = error, WindowsRestored = restored, Restoration = Restoration,
            Limits = new[] {
                "This is desktop composition capture, not a camera/photodiode measurement of physical scanout. Sub-frame visual latency is unresolved.",
                "Capture interval and copy duration are measured, not assumed. Screens are captured sequentially; their timestamps expose the skew.",
                "FirstExactReference is an observed upper bound from removal completion to an exact static-pixel match; it is not a pass/fail threshold. Null means no exact match in the observation window.",
                "Static edge pairs remain exactly unchanged during calibration. Inspect the native anchor image to verify that they belong to NexusWpp; wallpaper or desktop icons cannot establish application presence.",
                "Telemetry values, clock changes, antialiasing or color-management changes can reduce exact matches. Use regional errors and native frame evidence, not only the aggregate fraction.",
                "Opaque cover pixels are tracked separately; disappearance of a test window is never itself counted as scene recovery. Transparent coverage has no exact opaque marker.",
                "Normal tests use a fresh baseline. Rapid cycles reuse one baseline and perform no disk encoding or recalibration between cycles.",
                "All available monitors are covered in the full-coverage scenarios; partial and transparent cases deliberately leave real scene pixels visible.",
                "Harness capture consumes CPU/GPU. Collect idle performance in a separate run without this instrument."
            },
            Scenarios = Scenarios
        };
        File.WriteAllText(Path.Combine(output, "result.json"), serializer.Serialize(payload), new UTF8Encoding(false));
    }

    static void WriteIndex()
    {
        StringBuilder html = new StringBuilder("<!doctype html><meta charset=\"utf-8\"><title>NexusWpp real desktop capture</title><style>body{background:#111;color:#eee;font:16px system-ui;margin:24px}img{max-width:100%;height:auto}a{color:#7df}details{margin:20px 0}</style><h1>NexusWpp — pixels du bureau</h1><p>Les temps sont ceux des captures réelles. Les repères doivent être vérifiés dans l’image native; les pourcentages ne constituent pas un seuil de réussite.</p><p><a href=\"result.json\">Résultats et limites du protocole</a> · <a href=\"installed-package.json\">Version installée</a></p>");
        foreach (ScenarioResult scenario in Scenarios)
        {
            html.Append("<h2>").Append(scenario.Name).Append("</h2>");
            foreach (ScreenResult screen in scenario.Screens)
            {
                html.Append("<p>").Append(screen.DeviceName).Append(" · <a href=\"").Append(screen.ReferenceImagePath).Append("\">Référence native</a> · <a href=\"").Append(screen.AnchorImagePath).Append("\">Repères statiques</a></p>");
                html.Append("<details><summary>Captures de la transition</summary>");
                foreach (string strip in screen.Filmstrips) html.Append("<a href=\"").Append(strip).Append("\"><img loading=\"lazy\" src=\"").Append(strip).Append("\"></a>");
                html.Append("</details>");
                foreach (FrameMetric frame in screen.Frames.Where(f => f.NativeImagePath != null)) html.Append("<a href=\"").Append(frame.NativeImagePath).Append("\">Natif ").Append(Number(frame.RelativeToRemovalMilliseconds)).Append(" ms</a> · ");
            }
        }
        File.WriteAllText(Path.Combine(output, "index.html"), html.ToString(), new UTF8Encoding(false));
    }
}
