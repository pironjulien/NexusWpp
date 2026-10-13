using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace DesktopHtmlHost
{
    // Network identity, throughput and Wi-Fi signal. Identity (adapters, addresses, link speed,
    // gateway) is enumerated only when Windows reports an address or availability change, with a
    // 30 s safety refresh; throughput comes from the shared PDH sample (about 2 ms instead of
    // about 20 ms for NetworkInterface statistics on every tick).
    internal sealed class NetworkTelemetry
    {
        internal const string PdhReceived = @"\Network Interface(*)\Bytes Received/sec";
        internal const string PdhSent = @"\Network Interface(*)\Bytes Sent/sec";
        const int IdentityRefreshMs = 30000;

        sealed class Adapter
        {
            internal string Id, PdhInstance;
        }

        // Published values: empty / -1 when unknown, "none" when offline. Never a placeholder.
        internal long RxKBps = -1, TxKBps = -1, LinkSpeedMbps;
        internal string Ip = "", Ipv6 = "", Name = "", Type = "none", Gateway;

        readonly Stopwatch clock = Stopwatch.StartNew();
        volatile bool identityDirty = true;
        long lastIdentityMs;
        List<Adapter> adapters = new List<Adapter>();
        bool pdhAvailable;
        // Fallback when the PDH object is missing: byte counters differenced per interface Id, so
        // an adapter that reconnects never injects its whole cumulative counter.
        readonly Dictionary<string, long[]> previousBytes = new Dictionary<string, long[]>();
        long previousBytesMs = -1;
        bool errorLogged;

        internal NetworkTelemetry()
        {
            try
            {
                NetworkChange.NetworkAddressChanged += (s, e) => identityDirty = true;
                NetworkChange.NetworkAvailabilityChanged += (s, e) => identityDirty = true;
            }
            catch (Exception ex) { Program.LogDebug("Network change notifications unavailable: " + ex.Message); }
        }

        internal void AddCounters(PdhQuery perf)
        {
            pdhAvailable = perf != null && perf.Add(PdhReceived) && perf.Add(PdhSent);
        }

        // PDH instance names replace characters reserved in counter paths.
        internal static string PdhInstanceName(string description)
        {
            return (description ?? "").Replace('(', '[').Replace(')', ']').Replace('#', '_').Replace('/', '_').Replace('\\', '_');
        }

        // Virtual switches, VM adapters and VPN tunnels must not pollute the displayed identity
        // or double-count traffic that also flows through the physical adapter.
        internal static bool IsCountedAdapter(NetworkInterfaceType type, string description, string name)
        {
            int ifType = (int)type;
            // 24 loopback, 131 tunnel, 53 IF_TYPE_PROP_VIRTUAL (Tailscale, WireGuard, ...).
            if (ifType == 24 || ifType == 131 || ifType == 53) return false;
            string text = ((description ?? "") + " " + (name ?? "")).ToLowerInvariant();
            return !(text.Contains("virtual") || text.Contains("vmware") || text.Contains("virtualbox") ||
                     text.Contains("vbox") || text.Contains("hyper-v") || text.Contains("vethernet") ||
                     text.Contains("tap-") || text.Contains("wintun") || text.Contains("wireguard") ||
                     text.Contains("openvpn") || text.Contains("tailscale") || text.Contains("zerotier") ||
                     text.Contains("loopback") || text.Contains("bluetooth"));
        }

        // Called on the collection thread; perfSampled tells whether the shared PDH query was
        // collected for this tick.
        internal void Update(PdhQuery perf, bool perfSampled)
        {
            try
            {
                if (identityDirty || clock.ElapsedMilliseconds - lastIdentityMs >= IdentityRefreshMs) RefreshIdentity();

                if (pdhAvailable && perfSampled)
                {
                    var received = perf.Instances(PdhReceived);
                    var sent = perf.Instances(PdhSent);
                    if (received != null && sent != null)
                    {
                        RxKBps = SumKBps(received);
                        TxKBps = SumKBps(sent);
                        return;
                    }
                }
                UpdateFromInterfaceCounters();
            }
            catch (Exception ex)
            {
                RxKBps = TxKBps = -1;
                if (!errorLogged) { errorLogged = true; Program.LogDebug("Network telemetry error (logged once): " + ex.Message); }
            }
        }

        // Forget byte baselines after a long gap (pause, sleep) so the next rate is not an average
        // over the whole pause.
        internal void Rebase()
        {
            previousBytes.Clear();
            previousBytesMs = -1;
        }

        long SumKBps(List<KeyValuePair<string, double>> rows)
        {
            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var adapter in adapters) wanted.Add(adapter.PdhInstance);
            double total = 0;
            foreach (var row in rows)
                if (wanted.Contains(row.Key)) total += Math.Max(0, row.Value);
            return (long)(total / 1024.0);
        }

        void UpdateFromInterfaceCounters()
        {
            long now = clock.ElapsedMilliseconds;
            double seconds = previousBytesMs < 0 ? 0 : (now - previousBytesMs) / 1000.0;
            var current = new Dictionary<string, long[]>();
            long rx = 0, tx = 0;
            var counted = new HashSet<string>();
            foreach (var adapter in adapters) counted.Add(adapter.Id);
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!counted.Contains(ni.Id) || ni.OperationalStatus != OperationalStatus.Up) continue;
                var stats = ni.GetIPStatistics();
                var bytes = new[] { stats.BytesReceived, stats.BytesSent };
                current[ni.Id] = bytes;
                long[] before;
                if (previousBytes.TryGetValue(ni.Id, out before))
                {
                    rx += Math.Max(0, bytes[0] - before[0]);
                    tx += Math.Max(0, bytes[1] - before[1]);
                }
            }
            bool valid = seconds > 0.1 && seconds <= 2.5;
            RxKBps = valid ? (long)(rx / 1024.0 / seconds) : -1;
            TxKBps = valid ? (long)(tx / 1024.0 / seconds) : -1;
            previousBytes.Clear();
            foreach (var entry in current) previousBytes[entry.Key] = entry.Value;
            previousBytesMs = now;
        }

        void RefreshIdentity()
        {
            identityDirty = false;
            lastIdentityMs = clock.ElapsedMilliseconds;
            var counted = new List<Adapter>();
            string ip = "", ipv6 = "", name = "", type = "none", gateway = null;
            long linkMbps = 0;
            bool pinnedToGateway = false;

            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || !IsCountedAdapter(ni.NetworkInterfaceType, ni.Description, ni.Name)) continue;
                counted.Add(new Adapter { Id = ni.Id, PdhInstance = PdhInstanceName(ni.Description) });
                if (pinnedToGateway) continue;

                var ipProps = ni.GetIPProperties();
                string ipv4Gateway = null;
                foreach (GatewayIPAddressInformation gw in ipProps.GatewayAddresses)
                {
                    if (gw.Address.AddressFamily == AddressFamily.InterNetwork && !gw.Address.Equals(System.Net.IPAddress.Any)) { ipv4Gateway = gw.Address.ToString(); break; }
                }
                string v4 = null, v6 = null;
                foreach (var addr in ipProps.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork && v4 == null) v4 = addr.Address.ToString();
                    else if (addr.Address.AddressFamily == AddressFamily.InterNetworkV6 && v6 == null) v6 = addr.Address.ToString();
                }
                if (v4 == null) continue;
                // The interface holding the default gateway is the real internet-facing one.
                if (ip.Length == 0 || ipv4Gateway != null)
                {
                    ip = v4; ipv6 = v6 ?? ""; name = ni.Description; gateway = ipv4Gateway;
                    type = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : "Ethernet";
                    linkMbps = ni.Speed > 0 ? Math.Max(1, ni.Speed / 1000000L) : 0;
                    pinnedToGateway = ipv4Gateway != null;
                }
            }

            adapters = counted;
            Ip = ip; Ipv6 = ipv6; Name = name; Type = type; LinkSpeedMbps = linkMbps; Gateway = gateway;
        }

        // ---- Wi-Fi signal -------------------------------------------------------------------

        // wlan_intf_opcode_rssi: received signal strength in dBm. Unlike
        // wlan_intf_opcode_current_connection (SSID/BSSID), it does not reveal the location, so it
        // is not gated behind the location permission introduced in Windows 11 24H2.
        internal const int WlanOpcodeRssi = 0x10000102;
        const int ERROR_ACCESS_DENIED = 5;
        bool wlanUnavailable;
        long lastWifiMs = -1;
        int cachedWifiSignal = -1;

        // Same scale as the Windows signal quality: -100 dBm = 0 %, -50 dBm = 100 %.
        internal static int RssiToQuality(int dbm)
        {
            return Math.Max(0, Math.Min(100, 2 * (dbm + 100)));
        }

        // Signal quality (0-100) of the connected Wi-Fi interface, or -1.
        internal int WifiSignal()
        {
            if (Type != "Wi-Fi" || wlanUnavailable) return -1;
            if (lastWifiMs >= 0 && clock.ElapsedMilliseconds - lastWifiMs < 5000) return cachedWifiSignal;
            lastWifiMs = clock.ElapsedMilliseconds;

            IntPtr handle = IntPtr.Zero, listPtr = IntPtr.Zero;
            try
            {
                uint negotiatedVersion;
                if (WlanApi.WlanOpenHandle(2, IntPtr.Zero, out negotiatedVersion, out handle) != 0 ||
                    WlanApi.WlanEnumInterfaces(handle, IntPtr.Zero, out listPtr) != 0)
                {
                    return cachedWifiSignal = -1;
                }

                // WLAN_INTERFACE_INFO_LIST: dwNumberOfItems(4) + dwIndex(4) + items of
                // GUID(16) + description WCHAR[256](512) + state(4) = 532 bytes.
                int count = Marshal.ReadInt32(listPtr, 0);
                int best = -1;
                for (int i = 0; i < count; i++)
                {
                    IntPtr infoPtr = new IntPtr(listPtr.ToInt64() + 8 + ((long)i * 532));
                    if (Marshal.ReadInt32(infoPtr, 528) != 1) continue; // wlan_interface_state_connected

                    Guid ifaceGuid = (Guid)Marshal.PtrToStructure(infoPtr, typeof(Guid));
                    uint dataSize;
                    IntPtr dataPtr;
                    int status = WlanApi.WlanQueryInterface(handle, ref ifaceGuid, WlanOpcodeRssi, IntPtr.Zero, out dataSize, out dataPtr, IntPtr.Zero);
                    if (status == ERROR_ACCESS_DENIED)
                    {
                        // Never ask for more: the signal stays unavailable for this session.
                        wlanUnavailable = true;
                        Program.LogDebug("Wi-Fi signal unavailable: WLAN access denied.");
                        return cachedWifiSignal = -1;
                    }
                    if (status != 0) continue;
                    try
                    {
                        if (dataSize >= 4)
                        {
                            int dbm = Marshal.ReadInt32(dataPtr);
                            if (dbm < 0 && dbm > -128) best = Math.Max(best, RssiToQuality(dbm));
                        }
                    }
                    finally { WlanApi.WlanFreeMemory(dataPtr); }
                }
                return cachedWifiSignal = best;
            }
            catch (Exception)
            {
                // wlanapi.dll missing (Server Core) or WLAN service broken: stop querying.
                wlanUnavailable = true;
                return cachedWifiSignal = -1;
            }
            finally
            {
                if (listPtr != IntPtr.Zero) { try { WlanApi.WlanFreeMemory(listPtr); } catch { } }
                if (handle != IntPtr.Zero) { try { WlanApi.WlanCloseHandle(handle, IntPtr.Zero); } catch { } }
            }
        }
    }

    internal static class WlanApi
    {
        [DllImport("wlanapi.dll")]
        internal static extern int WlanOpenHandle(uint dwClientVersion, IntPtr pReserved, out uint pdwNegotiatedVersion, out IntPtr phClientHandle);

        [DllImport("wlanapi.dll")]
        internal static extern int WlanCloseHandle(IntPtr hClientHandle, IntPtr pReserved);

        [DllImport("wlanapi.dll")]
        internal static extern int WlanEnumInterfaces(IntPtr hClientHandle, IntPtr pReserved, out IntPtr ppInterfaceList);

        [DllImport("wlanapi.dll")]
        internal static extern int WlanQueryInterface(IntPtr hClientHandle, ref Guid pInterfaceGuid, int opCode, IntPtr pReserved, out uint pdwDataSize, out IntPtr ppData, IntPtr pWlanOpcodeValueType);

        [DllImport("wlanapi.dll")]
        internal static extern void WlanFreeMemory(IntPtr pMemory);
    }
}
