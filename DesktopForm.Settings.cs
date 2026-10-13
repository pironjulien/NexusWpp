using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.IO;
using System.Diagnostics;
using Microsoft.Win32;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using System.Management;
using System.Net.NetworkInformation;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Threading;

namespace DesktopHtmlHost
{
    // Tray icon, settings window and the request channel used when NexusWpp is launched again.
    public partial class DesktopForm
    {
        private void CreateTrayIcon()
        {
            bool french = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr";
            var menu = new ContextMenuStrip();
            menu.Items.Add(french ? "Réglages…" : "Settings…", null, delegate { ShowSettings(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(french ? "Quitter NexusWpp" : "Quit NexusWpp", null, delegate { QuitFromUser(); });
            trayIcon = new NotifyIcon
            {
                Text = "NexusWpp",
                ContextMenuStrip = menu,
                Visible = true
            };
            try { trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { trayIcon.Icon = System.Drawing.SystemIcons.Application; }
            trayIcon.MouseClick += (sender, e) => { if (e.Button == MouseButtons.Left) ShowSettings(); };
        }

        private void ListenForSettingsRequests()
        {
            try
            {
                showSettingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowSettingsEventName);
                showSettingsWait = ThreadPool.RegisterWaitForSingleObject(showSettingsEvent, (state, timedOut) =>
                {
                    TryBeginInvoke((MethodInvoker)ShowSettings);
                }, null, Timeout.Infinite, false);
            }
            catch (Exception ex)
            {
                Program.LogDebug("Settings request listener failed: " + ex.Message);
            }
        }

        private void ShowSettings()
        {
            if (isClosing) return;
            if (settingsForm != null && !settingsForm.IsDisposed)
            {
                if (settingsForm.WindowState == FormWindowState.Minimized) settingsForm.WindowState = FormWindowState.Normal;
                settingsForm.Activate();
                return;
            }
            string directory = Path.GetDirectoryName(htmlPath);
            if (string.IsNullOrEmpty(directory)) directory = AppDomain.CurrentDomain.BaseDirectory;
            settingsForm = new SettingsForm(Path.Combine(directory, "julienpiron.png"), PostSettings, QuitFromUser);
            settingsForm.FormClosed += delegate { settingsForm = null; };
            settingsForm.Show();
            settingsForm.Activate();
        }

        private void PostSettings()
        {
            if (isClosing) return;
            PostWebMessageAsJsonSafe(UserSettings.BuildSettingsMessage(), "Settings post");
        }

        private void QuitFromUser()
        {
            Program.QuitRequestedByUser = true;
            Program.LogDebug("Quit requested by the user.");
            if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.Close();
            Close();
        }
    }
}
