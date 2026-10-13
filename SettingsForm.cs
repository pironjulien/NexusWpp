using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DesktopHtmlHost
{
    // Settings window, opened from the tray icon or by launching NexusWpp again.
    public sealed class SettingsForm : Form
    {
        static readonly Color Back = Color.FromArgb(11, 15, 26);
        static readonly Color Panel = Color.FromArgb(20, 27, 43);
        static readonly Color Line = Color.FromArgb(38, 50, 74);
        static readonly Color Ink = Color.FromArgb(232, 238, 248);
        static readonly Color Muted = Color.FromArgb(140, 154, 178);
        static readonly Color Accent = Color.FromArgb(0, 199, 190);
        static readonly Color Danger = Color.FromArgb(255, 69, 96);

        static readonly bool French = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr";
        static string L(string fr, string en) { return French ? fr : en; }

        readonly string defaultLogoPath;
        readonly Action logoChanged;
        readonly Action quitRequested;
        readonly LogoPreview preview = new LogoPreview();
        readonly Button chooseButton, resetButton;
        readonly Label status;

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public SettingsForm(string defaultLogoPath, Action logoChanged, Action quitRequested)
        {
            this.defaultLogoPath = defaultLogoPath;
            this.logoChanged = logoChanged;
            this.quitRequested = quitRequested;

            // Auto-scaling to the display DPI only happens inside a SuspendLayout/ResumeLayout pair.
            SuspendLayout();
            Text = L("NexusWpp — Réglages", "NexusWpp — Settings");
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Back;
            ForeColor = Ink;
            Font = new Font("Segoe UI", 10F);
            // The window sizes itself to its content: no text can be clipped at any DPI or language.
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowOnly; // never jumps smaller when the status text changes
            Padding = new Padding(28, 20, 28, 22);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var layout = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty, BackColor = Back };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ContentWidth));

            var title = new Label { Text = "NEXUSWPP", Font = new Font("Bahnschrift", 18F, FontStyle.Bold), ForeColor = Ink, AutoSize = true, Margin = Padding.Empty };
            var subtitle = new Label { Text = L("Réglages", "Settings"), ForeColor = Muted, AutoSize = true, Margin = new Padding(2, 2, 0, 0) };
            var section = new Label { Text = L("LOGO CENTRAL", "CENTER LOGO"), Font = new Font("Bahnschrift", 10F, FontStyle.Bold), ForeColor = Accent, AutoSize = true, Margin = new Padding(2, 22, 0, 0) };

            preview.Size = new Size(180, 180);
            preview.Anchor = AnchorStyles.None; // centered in its row
            preview.Margin = new Padding(0, 12, 0, 18);
            preview.AccessibleRole = AccessibleRole.Graphic;
            preview.AccessibleName = L("Aperçu du logo central", "Center logo preview");

            chooseButton = MakeButton(L("Choisir une image…", "Choose an image…"), Accent, Back, 40);
            chooseButton.Click += ChooseImage;
            resetButton = MakeButton(L("Revenir au logo d'origine", "Restore the original logo"), Panel, Ink, 36);
            resetButton.Margin = new Padding(0, 10, 0, 0);
            resetButton.FlatAppearance.BorderColor = Line;
            resetButton.Click += ResetLogo;

            // Wraps to as many lines as the text needs within the column width.
            status = new Label { ForeColor = Muted, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 16),
                Text = L("L'image est recadrée en carré au centre, puis affichée dans le rond du radar.",
                         "The image is cropped to a centered square, then shown in the radar circle.") };

            var quitButton = MakeButton(L("Quitter NexusWpp", "Quit NexusWpp"), Back, Danger, 34);
            quitButton.Anchor = AnchorStyles.Left;
            quitButton.MinimumSize = new Size(170, 34);
            quitButton.FlatAppearance.BorderColor = Danger;
            quitButton.Click += delegate { Close(); quitRequested(); };
            var closeButton = MakeButton(L("Fermer", "Close"), Panel, Ink, 34);
            closeButton.Anchor = AnchorStyles.Right;
            closeButton.MinimumSize = new Size(110, 34);
            closeButton.FlatAppearance.BorderColor = Line;
            closeButton.Click += delegate { Close(); };
            CancelButton = closeButton;

            var footer = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Back };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            footer.Controls.Add(quitButton, 0, 0);
            footer.Controls.Add(closeButton, 1, 0);

            foreach (Control control in new Control[] { title, subtitle, section, preview, chooseButton, resetButton, status, footer })
            {
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.Controls.Add(control);
            }
            // Form padding only applies to docked children: place the content inside it explicitly.
            layout.Location = new Point(Padding.Left, Padding.Top);
            Controls.Add(layout);
            ResumeLayout(false);
            PerformLayout();
            RefreshPreview();
        }

        // Logical width of the content column (scaled with the DPI like everything else).
        const float ContentWidth = 384F;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dark = 1; // Windows 10 20H1+ / Windows 11 dark title bar; ignored elsewhere.
            try { DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); } catch { }
        }

        static Button MakeButton(string text, Color back, Color fore, int height)
        {
            // Full column width by default; grows with its text if a translation is longer.
            var button = new Button { Text = text, BackColor = back, ForeColor = fore, AutoSize = true, MinimumSize = new Size(0, height),
                Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = Padding.Empty, Padding = new Padding(12, 0, 12, 0),
                FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Font = new Font("Segoe UI Semibold", 10F) };
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = back;
            return button;
        }

        void RefreshPreview()
        {
            bool custom = UserSettings.HasCustomLogo;
            preview.SetImage(custom ? UserSettings.CustomLogoPath : defaultLogoPath);
            resetButton.Enabled = custom;
            resetButton.ForeColor = custom ? Ink : Muted;
        }

        void ChooseImage(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = L("Choisir le logo central", "Choose the center logo");
                dialog.Filter = L("Images", "Images") + " (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    UserSettings.ImportLogo(dialog.FileName);
                    status.ForeColor = Accent;
                    status.Text = L("Logo appliqué au fond d'écran.", "Logo applied to the wallpaper.");
                    RefreshPreview();
                    logoChanged();
                }
                catch (Exception ex)
                {
                    Program.LogDebug("Custom logo import failed: " + ex.Message);
                    status.ForeColor = Danger;
                    status.Text = L("Cette image n'a pas pu être lue. Essayez un fichier PNG ou JPEG.",
                                    "This image could not be read. Try a PNG or JPEG file.");
                }
            }
        }

        void ResetLogo(object sender, EventArgs e)
        {
            try
            {
                UserSettings.ResetLogo();
                status.ForeColor = Muted;
                status.Text = L("Logo d'origine rétabli.", "Original logo restored.");
                RefreshPreview();
                logoChanged();
            }
            catch (Exception ex)
            {
                Program.LogDebug("Custom logo reset failed: " + ex.Message);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) preview.SetImage(null);
            base.Dispose(disposing);
        }

        // Circular preview matching the radar's clipped center logo.
        sealed class LogoPreview : Control
        {
            Image image;

            public LogoPreview()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = Back;
            }

            public void SetImage(string path)
            {
                if (image != null) { image.Dispose(); image = null; }
                if (path != null && File.Exists(path))
                {
                    try { using (var stream = new MemoryStream(File.ReadAllBytes(path))) image = new Bitmap(Image.FromStream(stream)); }
                    catch (Exception ex) { Program.LogDebug("Logo preview failed: " + ex.Message); }
                }
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                var outer = new Rectangle(2, 2, Width - 5, Height - 5);
                var inner = Rectangle.Inflate(outer, -10, -10);
                using (var ring = new Pen(Accent, 3f)) g.DrawEllipse(ring, outer);
                using (var clip = new GraphicsPath())
                {
                    clip.AddEllipse(inner);
                    g.SetClip(clip);
                    using (var fill = new SolidBrush(Panel)) g.FillEllipse(fill, inner);
                    if (image != null) g.DrawImage(image, inner);
                    g.ResetClip();
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && image != null) { image.Dispose(); image = null; }
                base.Dispose(disposing);
            }
        }
    }
}
