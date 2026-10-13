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
    // Power plans and the telemetry timer that feeds the page.
    public partial class DesktopForm
    {
        private void SetPowerPlanFromUi(string guidStr)
        {
            bool success = false;
            string activeGuid = "";
            string error = "";

            try
            {
                if (telemetryCollector == null)
                {
                    error = "telemetry collector not ready";
                }
                else
                {
                    success = telemetryCollector.SetActivePowerPlan(guidStr, out activeGuid, out error);
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            Program.LogDebug(string.Format(CultureInfo.InvariantCulture,
                "Power plan request: requested={0}, active={1}, success={2}, error={3}",
                guidStr,
                activeGuid,
                success,
                error));

            try
            {
                string payload = string.Format(CultureInfo.InvariantCulture,
                    "{{\"control\":\"POWER_RESULT\",\"requestedGuid\":{0},\"activeGuid\":{1},\"success\":{2},\"error\":{3}}}",
                    JsonString(guidStr),
                    JsonString(activeGuid),
                    success ? "true" : "false",
                    JsonString(error));
                PostWebMessageAsJsonSafe(payload, "Power result post");
            }
            catch (Exception ex)
            {
                Program.LogDebug("Power result post error: " + ex.Message);
            }

            TelemetryTimer_Tick(null, null);
        }

        private static string JsonString(string value)
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
                    case '\r': sb.Append(@"\r"); break;
                    case '\n': sb.Append(@"\n"); break;
                    case '\t': sb.Append(@"\t"); break;
                    default:
                        if (c < 32) sb.Append("\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private void TelemetryTimer_Tick(object sender, EventArgs e)
        {
            if (isClosing || IsDisposed) return;
            if (runtimeSuspended) return;
            if (!telemetryReady || telemetryCollector == null || telemetryCollectPending) return;
            telemetryCollectPending = true;
            int generation = telemetryGeneration;

            try
            {
                System.Threading.ThreadPool.QueueUserWorkItem((state) =>
                {
                    string statsJson = "";
                    try
                    {
                        if (isClosing || IsDisposed || runtimeSuspended)
                        {
                            telemetryCollectPending = false;
                            return;
                        }
                        statsJson = telemetryCollector.CollectStats();
                        if (!TryBeginInvoke((MethodInvoker)delegate
                        {
                            try
                            {
                                if (!runtimeSuspended && generation == telemetryGeneration)
                                    PostWebMessageAsJsonSafe(statsJson, "Telemetry post");
                            }
                            catch (Exception ex)
                            {
                                Program.LogDebug("Telemetry post error: " + ex.Message + " | JSON: " + statsJson);
                            }
                            finally
                            {
                                telemetryCollectPending = false;
                                if (!runtimeSuspended && generation != telemetryGeneration)
                                    TelemetryTimer_Tick(null, null);
                            }
                        }))
                        {
                            telemetryCollectPending = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        Program.LogDebug("Telemetry collect error: " + ex.Message + " | JSON: " + statsJson);
                        try
                        {
                            if (!TryBeginInvoke((MethodInvoker)delegate
                            {
                                telemetryCollectPending = false;
                            }))
                            {
                                telemetryCollectPending = false;
                            }
                        }
                        catch
                        {
                            telemetryCollectPending = false;
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                telemetryCollectPending = false;
                Program.LogDebug("TelemetryTimer_Tick error: " + ex.Message);
            }
        }
    }
}
