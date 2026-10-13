using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DesktopHtmlHost;

// Custom center logo import and the settings window, without touching the user's real settings.
static class SettingsRegression
{
    static int failures;

    static void Check(bool condition, string label)
    {
        Console.WriteLine((condition ? "PASS " : "FAIL ") + label);
        if (!condition) failures++;
    }

    static List<Control> AllControls(Control root)
    {
        var all = new List<Control>();
        foreach (Control child in root.Controls) { all.Add(child); all.AddRange(AllControls(child)); }
        return all;
    }

    static bool Throws(Action action)
    {
        try { action(); return false; } catch { return true; }
    }

    [STAThread]
    static int Main(string[] args)
    {
        string output = args[0];
        string project = args[1];
        Directory.CreateDirectory(output);

        // Wide image: left third red, middle blue, right third green -> the centered square is blue.
        string wide = Path.Combine(output, "wide.png");
        using (var bitmap = new Bitmap(900, 300))
        using (var g = Graphics.FromImage(bitmap))
        {
            g.FillRectangle(Brushes.Red, 0, 0, 300, 300);
            g.FillRectangle(Brushes.Blue, 300, 0, 300, 300);
            g.FillRectangle(Brushes.Lime, 600, 0, 300, 300);
            bitmap.Save(wide, ImageFormat.Png);
        }
        string logo = Path.Combine(output, "custom-logo.png");
        UserSettings.ImportLogo(wide, logo);
        using (var stream = new MemoryStream(File.ReadAllBytes(logo)))
        using (var result = new Bitmap(stream))
        {
            Check(result.Width == UserSettings.LogoSize && result.Height == UserSettings.LogoSize, "logo is normalized to a 512 px square");
            Color center = result.GetPixel(256, 256), left = result.GetPixel(4, 256), right = result.GetPixel(507, 256);
            Check(center.B > 200 && center.R < 40 && left.B > 200 && right.B > 200, "logo keeps the centered square of a wide image");
        }

        string jpeg = Path.Combine(output, "photo.jpg");
        using (var bitmap = new Bitmap(300, 1200)) { using (var g = Graphics.FromImage(bitmap)) g.Clear(Color.Orange); bitmap.Save(jpeg, ImageFormat.Jpeg); }
        string logo2 = Path.Combine(output, "custom-logo-2.png");
        UserSettings.ImportLogo(jpeg, logo2);
        using (var stream = new MemoryStream(File.ReadAllBytes(logo2)))
        using (var result = new Bitmap(stream))
            Check(result.Width == 512 && result.Height == 512, "tall JPEG is accepted and squared");

        string tiny = Path.Combine(output, "tiny.png");
        using (var bitmap = new Bitmap(8, 8)) bitmap.Save(tiny, ImageFormat.Png);
        Check(Throws(() => UserSettings.ImportLogo(tiny, Path.Combine(output, "rejected-tiny.png"))), "too small image is rejected");
        Check(!File.Exists(Path.Combine(output, "rejected-tiny.png")), "rejected image writes no logo");

        string notImage = Path.Combine(output, "not-image.png");
        File.WriteAllText(notImage, "plain text, not an image");
        Check(Throws(() => UserSettings.ImportLogo(notImage, Path.Combine(output, "rejected-text.png"))), "non-image file is rejected");
        Check(Throws(() => UserSettings.ImportLogo(Path.Combine(output, "missing.png"), logo)), "missing file is rejected");

        // Replacing an existing logo keeps a valid file.
        UserSettings.ImportLogo(jpeg, logo);
        using (var stream = new MemoryStream(File.ReadAllBytes(logo)))
        using (var result = new Bitmap(stream))
            Check(result.GetPixel(256, 256).R > 200 && result.GetPixel(256, 256).B < 60, "importing again replaces the previous logo");

        string message = UserSettings.BuildSettingsMessage(logo);
        Check(message.StartsWith("{\"control\":\"SETTINGS\",\"logo\":\"data:image/png;base64,") && message.EndsWith("\"}"), "settings message carries the logo as a PNG data URL");
        Check(UserSettings.BuildSettingsMessage(null) == "{\"control\":\"SETTINGS\",\"logo\":null}", "default logo is sent as null");

        // Settings window renders off screen; the image is kept for a visual check.
        Application.EnableVisualStyles();
        using (var form = new SettingsForm(Path.Combine(project, "julienpiron.png"), () => { }, () => { }))
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-20000, -20000);
            form.Show();
            Application.DoEvents();
            using (var snapshot = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(snapshot, new Rectangle(0, 0, form.Width, form.Height));
                snapshot.Save(Path.Combine(output, "settings-window.png"), ImageFormat.Png);
            }
            Check(AllControls(form).Count(c => c is Button || c is Label) >= 7, "settings window builds its controls");
            form.Close();
        }

        // Windows display scaling: fonts grow in pixels and layout is scaled. No text may be clipped or covered,
        // even if the layout is not scaled at all (fonts only), which is what clipped the text in 1.0.37.
        foreach (bool scaleLayout in new[] { true, false })
        foreach (float scale in new[] { 1f, 1.25f, 1.5f, 1.75f, 2f })
        {
            using (var form = new SettingsForm(Path.Combine(project, "julienpiron.png"), () => { }, () => { }))
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-20000, -20000);
                foreach (var control in new[] { (Control)form }.Concat(AllControls(form)))
                    if (control.Font.Size > 0 && (control == form || control.Font != control.Parent.Font))
                        control.Font = new Font(control.Font.FontFamily, control.Font.Size * scale, control.Font.Style);
                if (scaleLayout) form.Scale(new SizeF(scale, scale));
                form.Show();
                Application.DoEvents();
                string label = " at " + (int)(scale * 100) + " %" + (scaleLayout ? "" : " (fonts only)");
                var texts = AllControls(form).Where(c => (c is Label || c is Button) && c.Visible).ToList();
                Check(texts.All(c => c.GetPreferredSize(new Size(c.Width, 0)).Height <= c.Height + 1 || c is Button && c.PreferredSize.Width <= c.Width + 1),
                    "every text fits its control" + label);
                var client = form.RectangleToScreen(form.ClientRectangle);
                Check(texts.All(c => client.Contains(c.RectangleToScreen(c.ClientRectangle))), "every text stays inside the window" + label);
                bool overlap = false;
                for (int i = 0; i < texts.Count; i++)
                    for (int j = i + 1; j < texts.Count; j++)
                        if (texts[i].RectangleToScreen(texts[i].ClientRectangle).IntersectsWith(texts[j].RectangleToScreen(texts[j].ClientRectangle))) overlap = true;
                Check(!overlap, "no text overlaps another" + label);
                if (scale == 1.5f && scaleLayout)
                    using (var snapshot = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(snapshot, new Rectangle(0, 0, form.Width, form.Height));
                        snapshot.Save(Path.Combine(output, "settings-window-150.png"), ImageFormat.Png);
                    }
                form.Close();
            }
        }

        Console.WriteLine(failures == 0 ? "RESULT settings regressions passed" : "RESULT " + failures + " settings regressions failed");
        return failures == 0 ? 0 : 1;
    }
}
