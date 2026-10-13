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
    // WebView2 control that tolerates disposal races during shutdown and recovery.
    public partial class DesktopForm
    {
        private sealed class ResilientWebView2 : WebView2
        {
            private bool shutdownStarted = false;

            // Raised (on the UI thread) when a lifecycle exception is suppressed outside a shutdown:
            // the CoreWebView2 behind this control is gone and the page is no longer updated.
            internal event EventHandler LifecycleFault;

            internal void BeginShutdown()
            {
                shutdownStarted = true;
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                try
                {
                    base.OnHandleCreated(e);
                }
                catch (ObjectDisposedException ex)
                {
                    if (!CanSuppressLifecycleException(ex))
                    {
                        throw;
                    }

                    LogSuppressedLifecycleException("handle creation", ex);
                }
                catch (InvalidOperationException ex)
                {
                    if (!CanSuppressLifecycleException(ex))
                    {
                        throw;
                    }

                    LogSuppressedLifecycleException("handle creation", ex);
                }
            }

            protected override void WndProc(ref Message m)
            {
                try
                {
                    base.WndProc(ref m);
                }
                catch (ObjectDisposedException ex)
                {
                    if (!CanSuppressLifecycleException(ex))
                    {
                        throw;
                    }

                    LogSuppressedLifecycleException("window message " + m.Msg.ToString(CultureInfo.InvariantCulture), ex);
                }
                catch (InvalidOperationException ex)
                {
                    if (!CanSuppressLifecycleException(ex))
                    {
                        throw;
                    }

                    LogSuppressedLifecycleException("window message " + m.Msg.ToString(CultureInfo.InvariantCulture), ex);
                }
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                try
                {
                    base.OnSizeChanged(e);
                }
                catch (ObjectDisposedException ex)
                {
                    if (!CanSuppressLifecycleException(ex))
                    {
                        throw;
                    }

                    LogSuppressedLifecycleException("resize", ex);
                }
                catch (InvalidOperationException ex)
                {
                    if (!CanSuppressLifecycleException(ex))
                    {
                        throw;
                    }

                    LogSuppressedLifecycleException("resize", ex);
                }
            }

            private bool CanSuppressLifecycleException(Exception ex)
            {
                if (ex is ObjectDisposedException)
                {
                    return shutdownStarted || IsDisposed || Disposing;
                }

                return IsDisposedLifecycleException(ex);
            }

            internal static bool IsDisposedLifecycleException(Exception ex)
            {
                InvalidOperationException invalid = ex as InvalidOperationException;
                if (invalid == null || string.IsNullOrEmpty(invalid.Message))
                {
                    return false;
                }

                return invalid.Message.IndexOf(
                    "CoreWebView2 members cannot be accessed after the WebView2 control is disposed",
                    StringComparison.OrdinalIgnoreCase) >= 0;
            }

            private void LogSuppressedLifecycleException(string context, Exception ex)
            {
                Program.LogDebug("Suppressed WebView2 " + context + " after disposal: " + ex.Message);
                if (shutdownStarted) return;
                EventHandler handler = LifecycleFault;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }
    }
}
