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

        const string StoreProductId = "9N2VCVDH7QDX";

        public SettingsForm(string defaultLogoPath, Action logoChanged, Action quitRequested, bool welcome = false)
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

            // First launch only: the wallpaper sits behind the desktop icons, so explain where it is.
            Label welcomeLabel = null;
            if (welcome)
            {
                welcomeLabel = new Label { Name = "welcome", BackColor = Panel, ForeColor = Ink, AutoSize = true, Dock = DockStyle.Fill,
                    Margin = new Padding(0, 22, 0, 0), Padding = new Padding(12, 10, 12, 10),
                    Text = L("Bienvenue ! NexusWpp tourne derrière vos icônes de bureau : Win+D pour le voir. " +
                             "Relancez NexusWpp depuis Démarrer pour revenir ici.",
                             "Welcome! NexusWpp runs behind your desktop icons: press Win+D to see it. " +
                             "Launch NexusWpp again from Start to come back here.") };
            }

            preview.Size = new Size(160, 160);
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
            status = new Label { Name = "status", ForeColor = Muted, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 16),
                Text = L("L'image (PNG, JPEG ou BMP) est recadrée en carré au centre, puis affichée dans le rond du radar.",
                         "The image (PNG, JPEG or BMP) is cropped to a centered square, then shown in the radar circle.") };
            // Screen readers announce import results without moving the focus.
            try { status.LiveSetting = System.Windows.Forms.Automation.AutomationLiveSetting.Polite; } catch { }

            // Application column: version, startup, rating and logs.
            var appSection = new Label { Text = L("APPLICATION", "APPLICATION"), Font = new Font("Bahnschrift", 10F, FontStyle.Bold), ForeColor = Accent, AutoSize = true, Margin = new Padding(2, 22, 0, 0) };
            var versionLabel = new Label { Name = "version", Text = "Version " + NexusWpp.ReleaseVersion.Value + (Program.IsPackaged ? " · Microsoft Store" : ""),
                ForeColor = Ink, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(2, 12, 0, 0) };
            var startupNote = new Label { Name = "startupNote", ForeColor = Muted, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(2, 8, 0, 10),
                Text = L("NexusWpp démarre avec Windows, réglable dans Paramètres > Applications > Démarrage.",
                         "NexusWpp starts with Windows; change it in Settings > Apps > Startup.") };
            var startupButton = MakeButton(L("Démarrage avec Windows…", "Startup settings…"), Panel, Ink, 36);
            startupButton.Name = "startup";
            startupButton.FlatAppearance.BorderColor = Line;
            startupButton.Click += delegate { OpenUri("ms-settings:startupapps"); };
            var rateButton = MakeButton(L("Noter NexusWpp", "Rate NexusWpp"), Panel, Ink, 36);
            rateButton.Name = "rate";
            rateButton.Margin = new Padding(0, 10, 0, 0);
            rateButton.FlatAppearance.BorderColor = Line;
            rateButton.Click += delegate { OpenUri(RatingUri(Program.IsPackaged)); };
            var logsButton = MakeButton(L("Dossier des journaux", "Open logs folder"), Panel, Ink, 36);
            logsButton.Name = "logs";
            logsButton.Margin = new Padding(0, 10, 0, 16);
            logsButton.FlatAppearance.BorderColor = Line;
            logsButton.Click += OpenLogsFolder;

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

            // Two columns side by side keep the window short enough for 1080p screens at 150 %.
            var logoColumn = Column(LogoColumnWidth, new Padding(0), section, preview, chooseButton, resetButton, status);
            var appColumn = Column(AppColumnWidth, new Padding(ColumnGap, 0, 0, 0), welcomeLabel, appSection, versionLabel, startupNote, startupButton, rateButton, logsButton);
            var body = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty, BackColor = Back };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.Controls.Add(logoColumn, 0, 0);
            body.Controls.Add(appColumn, 1, 0);

            foreach (Control control in new Control[] { title, subtitle, body, footer })
            {
                if (control == null) continue;
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

        // Logical widths of the columns (scaled with the DPI like everything else).
        const int LogoColumnWidth = 384, AppColumnWidth = 320, ColumnGap = 32;
        const float ContentWidth = LogoColumnWidth + ColumnGap + AppColumnWidth;

        static TableLayoutPanel Column(int width, Padding margin, params Control[] controls)
        {
            var column = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = margin, BackColor = Back, Anchor = AnchorStyles.Top | AnchorStyles.Left };
            column.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width));
            foreach (Control control in controls)
            {
                if (control == null) continue;
                column.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                column.Controls.Add(control);
            }
            return column;
        }

        internal static string RatingUri(bool packaged)
        {
            return packaged ? "ms-windows-store://review/?ProductId=" + StoreProductId
                            : "https://apps.microsoft.com/detail/" + StoreProductId;
        }

        void OpenUri(string uri)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true }); }
            catch (Exception ex)
            {
                Program.LogDebug("Opening " + uri + " failed: " + ex.Message);
                SetStatus(L("Windows n'a pas pu ouvrir cette page.", "Windows could not open this page."), Danger);
            }
        }

        void OpenLogsFolder(object sender, EventArgs e)
        {
            string folder = Program.GetLogsFolderForShell();
            try
            {
                Directory.CreateDirectory(folder);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Program.LogDebug("Opening the logs folder failed: " + ex.Message);
                SetStatus(L("Le dossier des journaux n'a pas pu être ouvert : ", "The logs folder could not be opened: ") + folder, Danger);
            }
        }

        void SetStatus(string text, Color color)
        {
            status.ForeColor = color;
            status.Text = text;
            try { status.AccessibilityObject.RaiseLiveRegionChanged(); } catch { }
        }

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

        async void ChooseImage(object sender, EventArgs e)
        {
            string fileName;
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = L("Choisir le logo central", "Choose the center logo");
                dialog.Filter = L("Images", "Images") + " (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                fileName = dialog.FileName;
            }

            // Decoding a large photo takes a moment: keep the window responsive meanwhile.
            chooseButton.Enabled = false;
            resetButton.Enabled = false;
            SetStatus(L("Import de l'image…", "Importing the image…"), Muted);
            Exception failure = null;
            try { await System.Threading.Tasks.Task.Run(() => UserSettings.ImportLogo(fileName)); }
            catch (Exception ex) { failure = ex; }
            if (IsDisposed) return;
            chooseButton.Enabled = true;
            if (failure == null)
            {
                SetStatus(L("Logo appliqué au fond d'écran.", "Logo applied to the wallpaper."), Accent);
                RefreshPreview();
                logoChanged();
            }
            else
            {
                Program.LogDebug("Custom logo import failed: " + failure.Message);
                RefreshPreview();
                SetStatus(L("Cette image n'a pas pu être utilisée. Choisissez un PNG, JPEG ou BMP de moins de 40 mégapixels.",
                            "This image could not be used. Choose a PNG, JPEG or BMP under 40 megapixels."), Danger);
            }
        }

        void ResetLogo(object sender, EventArgs e)
        {
            try
            {
                UserSettings.ResetLogo();
                SetStatus(L("Logo d'origine rétabli.", "Original logo restored."), Muted);
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
                // A picture, not an input: keyboard focus starts on the first button instead.
                SetStyle(ControlStyles.Selectable, false);
                TabStop = false;
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
