using System;
using System.Drawing;
using System.Globalization;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using DesktopHtmlHost;

// Real settings window captured from the screen, composited over a real dashboard render.
static class StoreSettingsShot
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT rect, int size);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

    [STAThread]
    static void Main(string[] args)
    {
        string defaultLogo = args[0], background = args[1], output = args[2];
        // The window follows the Windows display language; pin it to the screenshot's language.
        Thread.CurrentThread.CurrentUICulture = new CultureInfo(args.Length > 3 && args[3] == "en" ? "en-US" : "fr-FR");
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        using (var form = new SettingsForm(defaultLogo, () => { }, () => { }))
        {
            form.TopMost = true;
            form.StartPosition = FormStartPosition.CenterScreen;
            form.Show();
            for (int i = 0; i < 30; i++) { Application.DoEvents(); Thread.Sleep(30); }
            RECT r;
            DwmGetWindowAttribute(form.Handle, 9, out r, Marshal.SizeOf(typeof(RECT))); // extended frame bounds, no invisible borders
            var bounds = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            using (var window = new Bitmap(bounds.Width, bounds.Height))
            {
                using (var g = Graphics.FromImage(window)) g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                form.Close();
                using (var bg = new Bitmap(background))
                using (var canvas = new Bitmap(bg.Width, bg.Height, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(canvas))
                {
                    g.DrawImage(bg, 0, 0, bg.Width, bg.Height);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    float scale = bg.Height * 0.62f / window.Height;
                    int w = (int)(window.Width * scale), h = (int)(window.Height * scale);
                    int x = (bg.Width - w) / 2, y = (bg.Height - h) / 2;
                    using (var dim = new SolidBrush(Color.FromArgb(110, 0, 0, 0))) g.FillRectangle(dim, 0, 0, bg.Width, bg.Height);
                    for (int i = 24; i > 0; i -= 4)
                        using (var shadow = new SolidBrush(Color.FromArgb(10, 0, 0, 0))) g.FillRectangle(shadow, x - i, y - i + 12, w + 2 * i, h + 2 * i);
                    g.DrawImage(window, new Rectangle(x, y, w, h));
                    canvas.Save(output, ImageFormat.Png);
                }
            }
        }
        Console.WriteLine("SAVED " + output);
    }
}
