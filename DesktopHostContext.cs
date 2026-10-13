using System;
using System.Windows.Forms;

namespace DesktopHtmlHost
{
    // Implemented by the wallpaper form; lets the context tell a requested close from a lost window.
    internal interface IDesktopHostWindow
    {
        bool HandleLost { get; }
    }

    // Message loop owner. Not bound to the wallpaper form: when Explorer exits it destroys
    // WorkerW and its child windows, ours included. A main-form ApplicationContext would
    // then end the process silently; this one disposes the lost form and starts a new one,
    // which waits for the new WorkerW. The loop ends only after a requested close.
    internal sealed class DesktopHostContext : ApplicationContext
    {
        private const int RecreateDelayMs = 1000;
        private readonly Func<Form> createForm;
        private Form form;
        private Timer recreateTimer;

        internal DesktopHostContext(Func<Form> createForm)
        {
            this.createForm = createForm;
            StartForm();
        }

        internal Form CurrentForm { get { return form; } }

        private void StartForm()
        {
            form = createForm();
            form.FormClosed += Form_FormClosed;
            form.HandleDestroyed += Form_HandleDestroyed;
            // Application.Run(form) used to show the form; WebView2 renders only when visible.
            form.Show();
        }

        private void Form_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (ReferenceEquals(sender, form)) ExitThread();
        }

        private void Form_HandleDestroyed(object sender, EventArgs e)
        {
            Form lost = sender as Form;
            IDesktopHostWindow state = sender as IDesktopHostWindow;
            if (lost == null || state == null || !ReferenceEquals(lost, form) || lost.RecreatingHandle) return;
            if (lost.IsDisposed || lost.Disposing || !state.HandleLost) return; // normal close: FormClosed ends the loop

            lost.FormClosed -= Form_FormClosed;
            lost.HandleDestroyed -= Form_HandleDestroyed;
            form = null;
            if (recreateTimer == null)
            {
                recreateTimer = new Timer { Interval = RecreateDelayMs };
                recreateTimer.Tick += delegate { recreateTimer.Stop(); Recreate(); };
            }
            pendingDisposal = lost;
            recreateTimer.Start();
        }

        private Form pendingDisposal;

        private void Recreate()
        {
            Form lost = pendingDisposal;
            pendingDisposal = null;
            if (lost != null)
            {
                try { lost.Dispose(); }
                catch (Exception ex) { Program.LogDebug("Lost wallpaper form disposal: " + ex.Message); }
            }
            Program.LogDebug("Recreating the wallpaper window after the desktop parent was destroyed.");
            try
            {
                StartForm();
            }
            catch (Exception ex)
            {
                Program.LogDebug("Wallpaper window recreation failed: " + ex);
                ExitThread();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (recreateTimer != null) { recreateTimer.Dispose(); recreateTimer = null; }
                if (pendingDisposal != null) { try { pendingDisposal.Dispose(); } catch { } pendingDisposal = null; }
            }
            base.Dispose(disposing);
        }
    }
}
