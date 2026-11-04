using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Diagnostics;
using Microsoft.Win32;
using System.Management;
using System.Net.NetworkInformation;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Threading;
using System.Text.Json;

namespace DesktopHtmlHost
{
    public class TelemetryCollector
    {
        // Static / Boot cached specs
        public string MotherboardInfo = "";
        public string CpuInfo = "";
        public string CpuL2Cache = "";
        public string CpuL3Cache = "";
        public int CpuCores = 0;
        public int CpuLogical = 0;
        public string CpuBaseSpeed = "0.00";
        public int TotalRamGb = 0;
        public int RamSpeedMts = 0;
        public string RamType = "";
        public int RamModules = 0;
        public string NetworkName = "";
        public string IgpuInfo = "";
        public string IgpuDriver = "";
        public string IgpuDriverDate = "";
        public string DgpuDriver = "";
        public string DgpuDriverDate = "";
        public string GpuName = "";
        public string NpuName = "";
        public string NpuDriver = "";
        public string NpuDriverDate = "";
        public bool NpuDetected = false;
        public string DiskName = "";

        private string igpuLuid = "";
        private string dgpuLuid = "";
        private string npuLuid = "";
        private readonly List<string> directxAdapterLuids = new List<string>();
        private long igpuLuidValue, dgpuLuidValue;
        private string dgpuInstanceId = "";
        // Windows GPU memory budget of the iGPU (dedicated carve-out + shared system memory).
        private long igpuMemoryBudgetBytes;

        // Power panel buttons: Windows plans, or the Windows power mode overlays when the PC
        // exposes a single plan (Modern Standby laptops). Replaced atomically.
        public List<PowerPlanInfo> PowerPlans = new List<PowerPlanInfo>();

        // Historical rates
        private ulong prevIdleTime = 0;
        private ulong prevSystemTime = 0;
        // After a pause longer than this, rate counters are rebased instead of averaging the pause.
        private const long RebaseGapMs = 2500;
        private readonly Stopwatch collectClock = Stopwatch.StartNew();
        private long lastCollectMs = -1;
        private volatile int pingTime = -1;
        private volatile bool pingViaGateway;
        private DateTime lastPingTime = DateTime.MinValue;
        private const int PowerPlansRefreshSeconds = 5;
        private DateTime lastPowerPlansRefresh = DateTime.UtcNow;
        private volatile bool isPingPending = false;
        private DateTime pingStartedAt = DateTime.MinValue;

        // Process lists and cached top processes (ProcessRanking.cs)
        // Replaced as a whole by the ranking worker; read once per collection.
        private volatile ProcessUsage[] cachedTopProcesses = Array.Empty<ProcessUsage>();
        private volatile ProcessUsage cachedTopRamProcess;
        private int topProcessCounter = 3; // first ranking on the first collection
        private bool topProcessUpdatePending = false;

        private readonly NetworkTelemetry network = new NetworkTelemetry();

        // Slow perf counters cached separately so the UI can receive 2Hz payloads
        // without running every WMI query twice per second.
        private DateTime lastMemoryPerfTime = DateTime.MinValue;
        private long cachedRamCachedBytes = 0;
        private long cachedRamPoolPagedBytes = 0;
        private long cachedRamPoolNonPagedBytes = 0;
        private long cachedRamActivityVal = 0;
        private DateTime lastGpuPerfTime = DateTime.MinValue;
        private int cachedIgpuUtil = 0;
        private long cachedIgpuMemBytes = 0;
        private int cachedNpuUtil = 0;
        private long cachedNpuMemBytes = 0;
        private int cachedDgpuUtil = 0;
        private long cachedDgpuMemBytes = 0;
        private int cachedIgpuDecodeUtil = 0;

        // NVIDIA sensors come from NVML (NvidiaTelemetry.cs), the driver library itself.
        // Windows performance counters remain the fast utilization source.
        private readonly NvidiaTelemetry nvidiaTelemetry = new NvidiaTelemetry();
        private int cachedNpuTopPid = -1, cachedNpuTopPct = 0;
        private int cachedIgpuEncodeUtil = -1;
        private int cachedIgpuTopPid = -1, cachedIgpuTopPct = 0, cachedDgpuTopPid = -1, cachedDgpuTopPct = 0;

        // Busiest client of an adapter: {"name","pct"}; empty name = measured idle; null = no reading.
        private void WriteTopProcess(Utf8JsonWriter json, bool detected, bool fresh, int pid, int pct)
        {
            json.WritePropertyName("topProcess");
            if (!detected || !fresh) { json.WriteNullValue(); return; }
            string name = pid > 0 ? ProcessName(pid) : "";
            json.WriteStartObject();
            json.WriteString("name", name);
            json.WriteNumber("pct", name.Length > 0 ? pct : 0);
            json.WriteEndObject();
        }
        // pid -> name from the last ranking ("Process V2" instances), replaced as a whole every 4 s.
        private volatile Dictionary<int, string> rankedPidNames = new Dictionary<int, string>();
        private Dictionary<int, string> processNames = new Dictionary<int, string>();
        private Dictionary<int, string> processNamesSource;

        // Process names for pids seen in GPU counters. Pids are reused after exit, so names the ranking
        // did not cover are only kept until the next ranking.
        private string ProcessName(int pid)
        {
            var ranked = rankedPidNames;
            if (ranked.TryGetValue(pid, out string name)) return name;
            if (!ReferenceEquals(processNamesSource, ranked)) { processNames = new Dictionary<int, string>(); processNamesSource = ranked; }
            if (processNames.TryGetValue(pid, out name)) return name;
            try { using (var process = Process.GetProcessById(pid)) name = process.ProcessName; }
            catch (ArgumentException) { name = ""; } // the process has exited
            catch (InvalidOperationException) { name = ""; }
            processNames[pid] = name;
            return name;
        }

        // One persistent PDH query sampled once per collection (memory, system, CPU clock, GPU/NPU).
        private PdhQuery perf;
        private PdhQuery processPerf;
        private string pdhProcessCpu = ProcessRanking.CpuV2, pdhProcessPrivateBytes = ProcessRanking.RamV2;
        // Task Manager "Cached": the three standby lists plus the modified page list.
        private const string PdhModifiedPages = @"\Memory\Modified Page List Bytes";
        // Task Manager CPU utilization: work done relative to the base frequency (above 100 % in turbo, capped).
        private const string PdhCpuUtility = @"\Processor Information(_Total)\% Processor Utility";
        private const string PdhStandbyNormal = @"\Memory\Standby Cache Normal Priority Bytes";
        private const string PdhStandbyReserve = @"\Memory\Standby Cache Reserve Bytes";
        private const string PdhStandbyCore = @"\Memory\Standby Cache Core Bytes";
        private const string PdhPoolPaged = @"\Memory\Pool Paged Bytes";
        private const string PdhPoolNonPaged = @"\Memory\Pool Nonpaged Bytes";
        private const string PdhPageFaults = @"\Memory\Page Faults/sec";
        private const string PdhThreads = @"\System\Threads";
        private const string PdhProcesses = @"\System\Processes";
        private const string PdhCpuPerformance = @"\Processor Information(_Total)\% Processor Performance";
        private const string PdhCpuFrequency = @"\Processor Information(_Total)\Processor Frequency";
        // Same source as the WMI class Win32_PerfFormattedData_Counters_ThermalZoneInformation, in kelvins,
        // read from the query sampled every tick instead of waking the WMI provider host.
        private const string PdhThermalZones = @"\Thermal Zone Information(*)\Temperature";

        private void OpenPerformanceCounters()
        {
            perf = new PdhQuery();
            foreach (string path in new[] { PdhModifiedPages, PdhStandbyNormal, PdhStandbyReserve, PdhStandbyCore, PdhPoolPaged,
                PdhPoolNonPaged, PdhPageFaults, PdhThreads, PdhProcesses, PdhCpuPerformance, PdhCpuFrequency, PdhCpuUtility })
            {
                perf.Add(path);
            }
            // System drive activity: same semantics as the former raw WMI LogicalDisk differencing.
            if (!perf.Add(PdhThermalZones)) cpuThermalPerfUnavailable = true;
            diskCountersAvailable = perf.Add(PdhDiskRead) & perf.Add(PdhDiskWrite) & perf.Add(PdhDiskIdle) & perf.Add(PdhDiskLatency);
            network.AddCounters(perf);
            WindowsGpuTelemetry.AddCounters(perf);
            perf.Collect(); // baseline for rate counters

            // Process ranking has its own query: it is sampled every 4 s, so % Processor Time
            // is the average over that window rather than an instant.
            // "Process V2" (Windows 11) names instances "name:pid"; Windows 10 only has "Process".
            processPerf = new PdhQuery();
            if (!(processPerf.Add(pdhProcessCpu) && processPerf.Add(pdhProcessPrivateBytes)))
            {
                processPerf.Dispose();
                processPerf = new PdhQuery();
                pdhProcessCpu = ProcessRanking.CpuV1; pdhProcessPrivateBytes = ProcessRanking.RamV1;
                processPerf.Add(pdhProcessCpu);
                processPerf.Add(pdhProcessPrivateBytes);
            }
            processPerf.Collect();
        }

        // Windows may be installed on a drive other than C:; the value is validated
        // because it is interpolated into WMI queries.
        private static readonly string SystemDrive = ResolveSystemDrive();

        private static string ResolveSystemDrive()
        {
            string drive = Environment.GetEnvironmentVariable("SystemDrive");
            return !string.IsNullOrEmpty(drive) && System.Text.RegularExpressions.Regex.IsMatch(drive, "^[A-Za-z]:$")
                ? drive.ToUpperInvariant() : "C:";
        }
        private static readonly string PdhDiskRead = @"\LogicalDisk(" + SystemDrive + @")\Disk Read Bytes/sec";
        private static readonly string PdhDiskWrite = @"\LogicalDisk(" + SystemDrive + @")\Disk Write Bytes/sec";
        // Task Manager "Active time" is 100 - % Idle Time (% Disk Time is a queue length that exceeds 100).
        private static readonly string PdhDiskIdle = @"\LogicalDisk(" + SystemDrive + @")\% Idle Time";
        private static readonly string PdhDiskLatency = @"\LogicalDisk(" + SystemDrive + @")\Avg. Disk sec/Transfer";
        private bool diskCountersAvailable;
        private bool gpuCountersFresh;
        private DateTime lastGpuAttempt = DateTime.MinValue;
        private DateTime lastNpuDiscovery = DateTime.MinValue;

        // Driver-reported sensors of a non-NVIDIA discrete GPU (D3DKMT), read at most every 2 s.
        private WddmGpuSample wddmSample;
        private long wddmLastAttemptMs = -1, wddmLastSuccessMs = -1;

        // Real sensors state
        private long dgpuVramTotalBytes = 0;
        private bool cpuThermalPerfUnavailable = false;
        private bool cpuThermalAcpiUnavailable = false;
        private int thermalPerfEmptyReads;
        private DateTime lastCpuTempTime = DateTime.MinValue;
        private int cachedCpuTempC = -1;
        private string cpuTemperatureStatus = "unavailable";
        private DateTime lastAdapterDiscovery = DateTime.MinValue;

        // GPU names, LUIDs and memory sizes from the DirectX adapter registry (one source for all three),
        // classified by dedicated memory rather than by vendor name. Run at start, then once a minute (about 0.1 ms):
        // right after boot the driver may not have registered this boot's LUID yet, a driver restart or update gives
        // the adapter a new one, and a GPU can be enabled or plugged in at any time (Julien, 04/11: iGPU blank after
        // a start 16 s after boot). Returns true when the iGPU or the discrete GPU changed.
        private bool DiscoverDirectXAdapters()
        {
            lastAdapterDiscovery = DateTime.UtcNow;
            string previousIgpu = igpuLuid, previousDgpu = dgpuLuid;
            directxAdapterLuids.Clear();
            try
            {
                var adapters = new List<GpuAdapterInfo>();
                using (var rootKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\DirectX"))
                {
                    if (rootKey != null)
                    {
                        foreach (var subkeyName in rootKey.GetSubKeyNames())
                        {
                            using (var subkey = rootKey.OpenSubKey(subkeyName))
                            {
                                if (subkey == null) continue;
                                string desc = Convert.ToString(subkey.GetValue("Description") ?? "").Trim();
                                object luidValObj = subkey.GetValue("AdapterLuid");
                                if (luidValObj == null || string.IsNullOrEmpty(desc)) continue;
                                long luidVal = Convert.ToInt64(luidValObj);
                                // Entries of removed adapters keep the LUID of an older boot.
                                if (WddmGpuSensors.AdapterExists(luidVal) == false) continue;
                                string formattedLuid = GpuClassifier.FormatLuid(luidVal);
                                if (!directxAdapterLuids.Contains(formattedLuid)) directxAdapterLuids.Add(formattedLuid);
                                adapters.Add(new GpuAdapterInfo
                                {
                                    Name = desc, Luid = formattedLuid, LuidValue = luidVal,
                                    DedicatedBytes = Convert.ToInt64(subkey.GetValue("DedicatedVideoMemory") ?? 0L),
                                    SharedBytes = Convert.ToInt64(subkey.GetValue("SharedSystemMemory") ?? 0L)
                                });
                            }
                        }
                    }
                }
                GpuAdapterInfo igpu, dgpu;
                GpuClassifier.Select(adapters, out igpu, out dgpu);
                if (igpu != null)
                {
                    igpuLuid = igpu.Luid; igpuLuidValue = igpu.LuidValue; IgpuInfo = igpu.Name;
                    igpuMemoryBudgetBytes = igpu.DedicatedBytes + igpu.SharedBytes;
                }
                if (dgpu != null)
                {
                    dgpuLuid = dgpu.Luid; dgpuLuidValue = dgpu.LuidValue; GpuName = dgpu.Name;
                    dgpuVramTotalBytes = dgpu.DedicatedBytes;
                }
            }
            catch (Exception ex)
            {
                Program.LogDebug("DirectX Registry read error: " + ex.Message);
            }
            return igpuLuid != previousIgpu || dgpuLuid != previousDgpu;
        }

        public void Initialize()
        {
            try { OpenPerformanceCounters(); }
            catch (Exception ex) { DesktopHtmlHost.Program.LogDebug("Performance counters unavailable: " + ex.Message); }

            DiscoverDirectXAdapters();

            // Query CPU, Motherboard, RAM, GPU from WMI
            try
            {
                // Motherboard
                using (var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        object mfgVal = obj["Manufacturer"];
                        object prodVal = obj["Product"];
                        string mfg = mfgVal != null ? mfgVal.ToString() : "";
                        string prod = prodVal != null ? prodVal.ToString() : "";
                        mfg = mfg.Replace(" Co., Ltd.", "").Replace("ASUSTeK COMPUTER INC.", "ASUS");
                        MotherboardInfo = string.Format("{0} ({1})", mfg, prod);
                        break;
                    }
                }

                // CPU
                using (var searcher = new ManagementObjectSearcher("SELECT Name, L2CacheSize, L3CacheSize, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        object cpuNameVal = obj["Name"];
                        if (cpuNameVal != null) CpuInfo = cpuNameVal.ToString().Trim();
                        uint l2 = Convert.ToUInt32(obj["L2CacheSize"] ?? 0);
                        uint l3 = Convert.ToUInt32(obj["L3CacheSize"] ?? 0);
                        CpuL2Cache = string.Format("{0:F1} Mo", l2 / 1024.0);
                        CpuL3Cache = string.Format("{0:F1} Mo", l3 / 1024.0);
                        CpuCores = Convert.ToInt32(obj["NumberOfCores"] ?? CpuCores);
                        CpuLogical = Convert.ToInt32(obj["NumberOfLogicalProcessors"] ?? CpuLogical);
                        uint maxClock = Convert.ToUInt32(obj["MaxClockSpeed"] ?? 0);
                        CpuBaseSpeed = (maxClock / 1000.0).ToString("F2", CultureInfo.InvariantCulture);
                        break;
                    }
                }

                // RAM
                using (var searcher = new ManagementObjectSearcher("SELECT Capacity, Speed, SMBIOSMemoryType FROM Win32_PhysicalMemory"))
                {
                    ulong totalCapacity = 0;
                    uint speed = 0;
                    int modules = 0;
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        totalCapacity += Convert.ToUInt64(obj["Capacity"] ?? 0);
                        speed = Convert.ToUInt32(obj["Speed"] ?? speed);
                        modules++;
                        if (string.IsNullOrEmpty(RamType))
                        {
                            int smbiosType = Convert.ToInt32(obj["SMBIOSMemoryType"] ?? 0);
                            RamType = MemoryTypeFromSmbios(smbiosType);
                        }
                    }
                    if (totalCapacity > 0)
                    {
                        TotalRamGb = (int)Math.Round(totalCapacity / (1024.0 * 1024.0 * 1024.0));
                    }
                    if (speed > 0)
                    {
                        RamSpeedMts = (int)speed;
                    }
                    RamModules = modules;
                }

                ReadVideoControllers();

                using (var searcher = new ManagementObjectSearcher("SELECT DeviceName, DriverVersion, DriverDate FROM Win32_PnPSignedDriver WHERE DeviceName LIKE '%AI Boost%' OR DeviceName LIKE '%Neural%' OR DeviceName LIKE '%VPU%' OR DeviceName LIKE '%Inference%'"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        object nameVal = obj["DeviceName"];
                        object driverVerVal = obj["DriverVersion"];
                        object dateRawVal = obj["DriverDate"];
                        string name = nameVal != null ? nameVal.ToString() : "";
                        if (!IsLikelyNpuDeviceName(name))
                        {
                            continue;
                        }

                        NpuName = name;
                        NpuDetected = true;
                        NpuDriver = driverVerVal != null ? driverVerVal.ToString() : "";
                        NpuDriverDate = WmiDate(dateRawVal);
                        break;
                    }
                }

                try
                {
                    string partitionDeviceId = "";
                    using (var searcher = new ManagementObjectSearcher("ASSOCIATORS OF {Win32_LogicalDisk.DeviceID='" + SystemDrive + "'} WHERE AssocClass=Win32_LogicalDiskToPartition"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            object deviceId = obj["DeviceID"];
                            if (deviceId != null)
                            {
                                partitionDeviceId = deviceId.ToString().Replace("\\", "\\\\").Replace("'", "\\'");
                                break;
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(partitionDeviceId))
                    {
                        string query = string.Format(CultureInfo.InvariantCulture, "ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{0}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition", partitionDeviceId);
                        using (var searcher = new ManagementObjectSearcher(query))
                        {
                            foreach (ManagementObject obj in searcher.Get())
                            {
                                object model = obj["Model"];
                                if (model != null)
                                {
                                    DiskName = model.ToString().Trim();
                                    break;
                                }
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(DiskName))
                    {
                        using (var searcher = new ManagementObjectSearcher("SELECT Model FROM Win32_DiskDrive"))
                        {
                            foreach (ManagementObject obj in searcher.Get())
                            {
                                object model = obj["Model"];
                                if (model != null)
                                {
                                    DiskName = model.ToString().Trim();
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Program.LogDebug("Disk name detection error: " + ex.Message);
                }

                if (!NpuDetected)
                {
                    using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_PnPEntity WHERE Name LIKE '%AI Boost%' OR Name LIKE '%Neural%' OR Name LIKE '%VPU%' OR Name LIKE '%Inference%'"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            object nameVal = obj["Name"];
                            string name = nameVal != null ? nameVal.ToString() : "";
                            if (IsLikelyNpuDeviceName(name))
                            {
                                NpuName = name;
                                NpuDetected = true;
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Program.LogDebug("Telemetry init error: " + ex.Message);
            }

            if (dgpuVramTotalBytes <= 0) ReadDgpuVramTotalFromRegistry();

            // Initialize power plans
            UpdatePowerPlansCache();
        }

        // Driver version, date and device instance of the iGPU and discrete GPU. Called at start and again when
        // the adapter discovery finds a new or changed adapter (late registration, driver update).
        private void ReadVideoControllers()
        {
            try
            {
                bool registryClassified = !string.IsNullOrEmpty(GpuName) || !string.IsNullOrEmpty(IgpuInfo);
                using (var searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion, DriverDate, PNPDeviceID FROM Win32_VideoController"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string name = Convert.ToString(obj["Name"] ?? "");
                        string driverVer = Convert.ToString(obj["DriverVersion"] ?? "");
                        string driverDate = WmiDate(obj["DriverDate"]);
                        string instanceId = Convert.ToString(obj["PNPDeviceID"] ?? "");

                        // Driver details attach to the adapters classified from the registry;
                        // without the registry, the same classifier works from the name alone.
                        string kind = registryClassified
                            ? (name.Equals(GpuName, StringComparison.OrdinalIgnoreCase) ? "dgpu" : name.Equals(IgpuInfo, StringComparison.OrdinalIgnoreCase) ? "igpu" : "")
                            : GpuClassifier.ClassifyGpu(name, 0);
                        if (kind == "dgpu" && (registryClassified || string.IsNullOrEmpty(GpuName)))
                        {
                            GpuName = name;
                            DgpuDriver = driverVer;
                            DgpuDriverDate = driverDate;
                            dgpuInstanceId = instanceId;
                        }
                        else if (kind == "igpu" && (registryClassified || string.IsNullOrEmpty(IgpuInfo)))
                        {
                            IgpuInfo = name;
                            IgpuDriver = driverVer;
                            IgpuDriverDate = driverDate;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is ManagementException || ex is System.Runtime.InteropServices.COMException || ex is UnauthorizedAccessException)
            {
                Program.LogDebug("Video controller query error: " + ex.Message);
            }
        }

        // CIM datetime "20260903000000.000000-000" -> "03/09/2026"; empty when absent or malformed.
        internal static string WmiDate(object value)
        {
            string raw = Convert.ToString(value ?? "");
            return raw.Length >= 8 && DateTime.TryParseExact(raw.Substring(0, 8), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date)
                ? date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "";
        }

        private static string MemoryTypeFromSmbios(int smbiosType)
        {
            switch (smbiosType)
            {
                case 20: return "DDR";
                case 21: return "DDR2";
                case 24: return "DDR3";
                case 26: return "DDR4";
                case 27: return "LPDDR";
                case 28: return "LPDDR2";
                case 29: return "LPDDR3";
                case 30: return "LPDDR4";
                case 34: return "DDR5";
                case 35: return "LPDDR5";
                default: return "";
            }
        }

        // Reads the dedicated VRAM size reported by the display driver (real hardware value).
        private void ReadDgpuVramTotalFromRegistry()
        {
            if (string.IsNullOrEmpty(GpuName)) return;

            try
            {
                using (var classKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (classKey == null) return;

                    foreach (var subkeyName in classKey.GetSubKeyNames())
                    {
                        using (var subkey = classKey.OpenSubKey(subkeyName))
                        {
                            if (subkey == null) continue;
                            string desc = Convert.ToString(subkey.GetValue("DriverDesc") ?? "");
                            if (string.IsNullOrEmpty(desc)) continue;
                            if (!desc.Equals(GpuName, StringComparison.OrdinalIgnoreCase)) continue;

                            object qw = subkey.GetValue("HardwareInformation.qwMemorySize");
                            if (qw != null)
                            {
                                long bytes = Convert.ToInt64(qw);
                                if (bytes > 0)
                                {
                                    dgpuVramTotalBytes = bytes;
                                    return;
                                }
                            }

                            object dw = subkey.GetValue("HardwareInformation.MemorySize");
                            byte[] raw = dw as byte[];
                            if (raw != null && raw.Length >= 4)
                            {
                                long bytes = BitConverter.ToUInt32(raw, 0);
                                if (bytes > 0)
                                {
                                    dgpuVramTotalBytes = bytes;
                                    return;
                                }
                            }
                            else if (dw != null)
                            {
                                long bytes = Convert.ToInt64(dw);
                                if (bytes > 0)
                                {
                                    dgpuVramTotalBytes = bytes;
                                    return;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Program.LogDebug("VRAM registry read error: " + ex.Message);
            }
        }

        // Real CPU temperature. Tries the thermal zone performance counter first because it is
        // readable without administrator rights, then the ACPI WMI class. Returns -1 when no
        // sensor is exposed so the UI can substitute another real metric.
        private int GetCpuTemperature()
        {
            if (lastCpuTempTime != DateTime.MinValue && (DateTime.Now - lastCpuTempTime).TotalSeconds < 5.0)
            {
                return cachedCpuTempC;
            }
            lastCpuTempTime = DateTime.Now;

            double maxCelsius = double.MinValue;

            if (!cpuThermalPerfUnavailable)
            {
                try
                {
                    var zones = perf != null ? perf.Instances(PdhThermalZones) : null;
                    if (zones != null)
                    {
                        foreach (var zone in zones)
                        {
                            double celsius = zone.Value - 273.15;
                            if (celsius > maxCelsius) maxCelsius = celsius;
                        }
                    }
                    // A machine without a usable thermal zone stops paying for this query.
                    thermalPerfEmptyReads = maxCelsius > 5.0 && maxCelsius < 120.0 ? 0 : thermalPerfEmptyReads + 1;
                    if (thermalPerfEmptyReads >= 3) cpuThermalPerfUnavailable = true;
                }
                catch
                {
                    cpuThermalPerfUnavailable = true;
                }
            }

            if ((maxCelsius <= 5.0 || maxCelsius >= 120.0) && !cpuThermalAcpiUnavailable)
            {
                try
                {
                    using (var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            double deciKelvin = Convert.ToDouble(obj["CurrentTemperature"] ?? 0.0);
                            double celsius = (deciKelvin / 10.0) - 273.15;
                            if (celsius > maxCelsius) maxCelsius = celsius;
                        }
                    }
                }
                catch
                {
                    // Usually access denied without elevation: stop querying this source.
                    cpuThermalAcpiUnavailable = true;
                }
            }

            bool previouslyAvailable = cachedCpuTempC >= 0 || cpuTemperatureStatus == "stale";
            cachedCpuTempC = (maxCelsius > 5.0 && maxCelsius < 120.0) ? (int)Math.Round(maxCelsius) : -1;
            cpuTemperatureStatus = cachedCpuTempC >= 0 ? "fresh" : previouslyAvailable ? "stale" : "unavailable";
            return cachedCpuTempC;
        }

        private static bool IsLikelyNpuDeviceName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            string lower = name.ToLowerInvariant();
            return lower.Contains("ai boost") ||
                   lower.Contains("neural") ||
                   lower.Contains("inference") ||
                   lower.Contains(" vpu") ||
                   lower.Contains("(vpu") ||
                   lower.Equals("npu") ||
                   lower.StartsWith("npu ") ||
                   lower.Contains(" npu ") ||
                   lower.Contains("(npu");
        }

        private string DiscoverNpuLuid()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(@"root\cimv2", "SELECT Name FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine WHERE Name LIKE '%engtype_neural%' OR Name LIKE '%engtype_compute%'"))
                {
                    searcher.Options.Timeout = TimeSpan.FromSeconds(2);
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string name = Convert.ToString(obj["Name"] ?? "");
                        string luid = ExtractLuidFromCounterName(name);
                        if (string.IsNullOrEmpty(luid))
                        {
                            continue;
                        }
                        if (luid.Equals(igpuLuid, StringComparison.OrdinalIgnoreCase) ||
                            luid.Equals(dgpuLuid, StringComparison.OrdinalIgnoreCase) ||
                            directxAdapterLuids.Contains(luid))
                        {
                            continue;
                        }
                        return luid;
                    }
                }
            }
            catch {}

            return "";
        }

        private static string ExtractLuidFromCounterName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "";
            }

            int start = name.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return "";
            }
            start += 5;
            int end = name.IndexOf("_phys_", start, StringComparison.OrdinalIgnoreCase);
            if (end <= start)
            {
                return "";
            }
            return name.Substring(start, end - start).ToLowerInvariant();
        }

        public void UpdatePowerPlansCache(bool allowPowercfg = true)
        {
            List<PowerPlanInfo> plans = PowerSchemes.EnumeratePlans() ?? (allowPowercfg ? ReadPowerPlansFromPowercfg() : null);
            if (plans == null || plans.Count == 0) return;

            // A single plan (usual on Modern Standby laptops) makes a one-button panel that does
            // nothing: the real control there is the Windows power mode, applied as an overlay.
            if (plans.Count == 1)
            {
                string overlay = PowerSchemes.EffectiveOverlay();
                if (overlay != null)
                {
                    PowerPlans = PowerSchemes.OverlayModes(overlay, CultureInfo.CurrentUICulture);
                    return;
                }
            }

            string active = GetActivePowerPlanGuid();
            foreach (var plan in plans) plan.Active = plan.Guid.Equals(active, StringComparison.OrdinalIgnoreCase);
            PowerPlans = PowerSchemes.Sort(plans);
        }

        // Console tools write in the OEM code page (850 in French), which .NET only knows through the code pages provider.
        private static Encoding OemEncoding()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }

        // Fallback when powrprof cannot enumerate: powercfg from System32 (never resolved through PATH).
        private static List<PowerPlanInfo> ReadPowerPlansFromPowercfg()
        {
            try
            {
                var newPlans = new List<PowerPlanInfo>();
                string powercfg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "powercfg.exe");
                ProcessStartInfo startInfo = new ProcessStartInfo(powercfg, "/list")
                {
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = OemEncoding(),
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(startInfo))
                {
                    // A stuck powercfg must not block the caller (UI thread on plan changes).
                    var read = p.StandardOutput.ReadToEndAsync();
                    if (!read.Wait(3000))
                    {
                        try { p.Kill(); } catch {}
                        Program.LogDebug("powercfg /list timed out.");
                        return null;
                    }
                    string output = read.Result;
                    p.WaitForExit(2000);
                    string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(line, @"GUID.*:\s*([a-f0-9\-]+)\s*\(([^)]+)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            string guid = match.Groups[1].Value.Trim().ToLowerInvariant();
                            string name = match.Groups[2].Value.Trim();
                            newPlans.Add(new PowerPlanInfo { Guid = guid, Name = name, Kind = PowerSchemes.KindFromGuid(guid) ?? PowerSchemes.KindFromName(name) ?? "balanced" });
                        }
                    }
                }
                return newPlans.Count > 0 ? newPlans : null;
            }
            catch (Exception ex)
            {
                Program.LogDebug("powercfg /list error: " + ex.Message);
                return null;
            }
        }

        // Marks the active plan or power mode; cheap enough for every collection.
        private void RefreshActivePowerPlan(List<PowerPlanInfo> plans)
        {
            bool overlays = plans.Count > 0 && plans[0].Overlay;
            string active = overlays ? (PowerSchemes.EffectiveOverlay() ?? "") : GetActivePowerPlanGuid();
            foreach (var plan in plans) plan.Active = plan.Guid.Equals(active, StringComparison.OrdinalIgnoreCase);
        }

        public string GetActivePowerPlanGuid()
        {
            try
            {
                IntPtr activeGuidPtr;
                uint res = Win32.PowerGetActiveScheme(IntPtr.Zero, out activeGuidPtr);
                if (res == 0 && activeGuidPtr != IntPtr.Zero)
                {
                    Guid activeGuid = Marshal.PtrToStructure<Guid>(activeGuidPtr);
                    Win32.LocalFree(activeGuidPtr);
                    return activeGuid.ToString();
                }
            }
            catch {}

            return "";
        }

        public bool SetActivePowerPlan(string guidStr, out string activeGuid, out string error)
        {
            activeGuid = "";
            error = "";

            Guid requestedGuid;
            if (!Guid.TryParse(guidStr, out requestedGuid))
            {
                error = "invalid GUID";
                activeGuid = GetActivePowerPlanGuid();
                return false;
            }

            if (PowerPlans.Count > 0 && !PowerPlans.Any(p => p.Guid.Equals(guidStr, StringComparison.OrdinalIgnoreCase)))
            {
                UpdatePowerPlansCache();
                if (!PowerPlans.Any(p => p.Guid.Equals(guidStr, StringComparison.OrdinalIgnoreCase)))
                {
                    error = "GUID not found in the power plans";
                    activeGuid = GetActivePowerPlanGuid();
                    return false;
                }
            }

            var plans = PowerPlans;
            var target = plans.FirstOrDefault(p => p.Guid.Equals(guidStr, StringComparison.OrdinalIgnoreCase));
            bool overlay = target != null && target.Overlay;
            uint result = overlay ? PowerSchemes.SetOverlay(target.Guid) : Win32.PowerSetActiveScheme(IntPtr.Zero, requestedGuid);
            if (result != 0)
            {
                error = (overlay ? "PowerSetActiveOverlayScheme=" : "PowerSetActiveScheme=") + result.ToString(CultureInfo.InvariantCulture);
            }

            Thread.Sleep(120);
            activeGuid = overlay ? (PowerSchemes.EffectiveOverlay() ?? "") : GetActivePowerPlanGuid();
            bool success = activeGuid.Equals(guidStr, StringComparison.OrdinalIgnoreCase);
            if (success)
            {
                foreach (var plan in plans)
                {
                    plan.Active = plan.Guid.Equals(activeGuid, StringComparison.OrdinalIgnoreCase);
                }
            }
            else if (string.IsNullOrEmpty(error))
            {
                error = "active scheme did not match request";
            }

            return success;
        }

        private string lastPingTarget;

        private void UpdatePing()
        {
            // A lost PingCompleted callback must not freeze the reading forever.
            if (isPingPending && (DateTime.Now - pingStartedAt).TotalSeconds < 10.0) return;
            if (lastPingTime != DateTime.MinValue && (DateTime.Now - lastPingTime).TotalSeconds < 5.0) return;

            lastPingTime = DateTime.Now;
            isPingPending = true;
            pingStartedAt = DateTime.Now;
            // Internet targets always come first (the last one that answered leads); the
            // gateway is a labelled last resort so LAN latency never poses as internet latency.
            var targets = new List<string> { "1.1.1.1", "8.8.8.8" };
            if (lastPingTarget != null && targets.Remove(lastPingTarget)) targets.Insert(0, lastPingTarget);
            // Corporate and captive networks often drop public ICMP; the router still answers.
            string gateway = network.Gateway;
            if (gateway != null) targets.Add(gateway);
            PingNext(targets, 0, gateway);
        }

        private void PingNext(List<string> targets, int index, string gateway)
        {
            try
            {
                string target = targets[index];
                Ping pingSender = new Ping();
                pingSender.PingCompleted += (s, e) =>
                {
                    try { ((Ping)s).Dispose(); } catch {}
                    if (e.Reply != null && e.Reply.Status == IPStatus.Success)
                    {
                        bool viaGateway = target == gateway;
                        if (!viaGateway) lastPingTarget = target;
                        pingViaGateway = viaGateway;
                        pingTime = (int)e.Reply.RoundtripTime;
                        isPingPending = false;
                    }
                    else if (index + 1 < targets.Count)
                    {
                        PingNext(targets, index + 1, gateway);
                    }
                    else
                    {
                        pingTime = -1;
                        isPingPending = false;
                    }
                };
                pingSender.SendAsync(target, 1000, null);
            }
            catch
            {
                if (index + 1 < targets.Count) PingNext(targets, index + 1, gateway);
                else isPingPending = false;
            }
        }

        private void UpdateTopProcesses()
        {
            topProcessCounter++;
            if (topProcessCounter < 4) return; // run once every 4 seconds
            topProcessCounter = 0;
            if (topProcessUpdatePending) return;
            topProcessUpdatePending = true;

            System.Threading.ThreadPool.QueueUserWorkItem((state) =>
            {
                try
                {
                    if (processPerf == null || !processPerf.Collect()) return;
                    var cpuRows = processPerf.Instances(pdhProcessCpu);
                    var ramRows = processPerf.Instances(pdhProcessPrivateBytes);
                    if (cpuRows == null || ramRows == null) return;
                    var pidNames = new Dictionary<int, string>();
                    ProcessRanking.Rank(cpuRows, ramRows, Environment.ProcessorCount, out var top, out var topRam, pidNames);
                    cachedTopProcesses = top;
                    cachedTopRamProcess = topRam;
                    // Also on Windows 10 (no pid in the names): the lookup cache then restarts every 4 s.
                    rankedPidNames = pidNames;
                }
                catch (Exception ex)
                {
                    Program.LogDebug("UpdateTopProcesses error: " + ex.Message);
                }
                finally
                {
                    topProcessUpdatePending = false;
                }
            });
        }

        // Rate readings (CPU load, PDH rates, network bytes) are deltas since the previous sample.
        // After a pause, sleep or covered desktop they would average the whole gap, so the
        // baselines are retaken and a short fresh window is measured instead.
        private void RebaseRateCounters()
        {
            if (Win32.GetSystemTimes(out ulong idleTime, out ulong kernelTime, out ulong userTime))
            {
                prevIdleTime = idleTime;
                prevSystemTime = kernelTime + userTime;
            }
            if (perf != null) perf.Collect();
            // The ranking worker only starts from this thread, so no sample is in flight here.
            if (processPerf != null && !topProcessUpdatePending) processPerf.Collect();
            network.Rebase();
            Thread.Sleep(250);
        }

        public string CollectStats()
        {
            long gapStartMs = collectClock.ElapsedMilliseconds;
            if (lastCollectMs >= 0 && gapStartMs - lastCollectMs > RebaseGapMs) RebaseRateCounters();
            lastCollectMs = collectClock.ElapsedMilliseconds;

            // Gather CPU Load
            int cpuLoad = -1;
            if (Win32.GetSystemTimes(out ulong idle, out ulong kernel, out ulong user))
            {
                ulong system = kernel + user;

                if (prevSystemTime > 0)
                {
                    ulong systemDiff = system - prevSystemTime;
                    ulong idleDiff = idle - prevIdleTime;
                    if (systemDiff > 0)
                    {
                        double load = 1.0 - (double)idleDiff / systemDiff;
                        cpuLoad = (int)Math.Max(0, Math.Min(100, Math.Round(load * 100)));
                    }
                }
                prevIdleTime = idle;
                prevSystemTime = system;
            }

            // Real ACPI sensor; -1 when the machine does not expose a thermal zone.
            int cpuTemp = GetCpuTemperature();

            // RAM usage
            int ramLoad = 0;
            ulong ramTotalPhys = 0;
            ulong ramFreePhys = 0;
            ulong ramTotalPage = 0;
            ulong ramAvailPage = 0;
            MEMORYSTATUSEX memStatus = new MEMORYSTATUSEX();
            if (Win32.GlobalMemoryStatusEx(ref memStatus))
            {
                ramLoad = (int)memStatus.dwMemoryLoad;
                ramTotalPhys = memStatus.ullTotalPhys;
                ramFreePhys = memStatus.ullAvailPhys;
                ramTotalPage = memStatus.ullTotalPageFile;
                ramAvailPage = memStatus.ullAvailPageFile;
            }

            // Disk Space
            double diskFreeGb = 0.0;
            double diskTotalGb = 0.0;
            int diskStoragePercent = 0;
            try
            {
                var drive = new DriveInfo(SystemDrive);
                diskFreeGb = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                diskTotalGb = drive.TotalSize / (1024.0 * 1024.0 * 1024.0);
                if (drive.TotalSize > 0)
                    diskStoragePercent = (int)Math.Round((1.0 - (double)drive.AvailableFreeSpace / drive.TotalSize) * 100);
            }
            catch {}

            // One PDH sample feeds memory, system, CPU clock, disk, network and GPU/NPU readings.
            long commitUsedBytes = (long)(ramTotalPage - ramAvailPage);
            long commitLimitBytes = (long)ramTotalPage;
            long ramCachedBytes = cachedRamCachedBytes;
            long ramPoolPagedBytes = cachedRamPoolPagedBytes;
            long ramPoolNonPagedBytes = cachedRamPoolNonPagedBytes;
            long ramActivityVal = cachedRamActivityVal;
            bool perfSampled = perf != null && perf.Collect();

            network.Update(perf, perfSampled);
            if (perfSampled)
            {
                // Task Manager CPU utilization when the counter exists; GetSystemTimes (busy time) otherwise.
                double? utility = perf.Value(PdhCpuUtility);
                if (utility.HasValue) cpuLoad = (int)Math.Round(Math.Max(0, Math.Min(100, utility.Value)));

                double? modified = perf.Value(PdhModifiedPages);
                if (modified.HasValue)
                {
                    ramCachedBytes = (long)(modified.Value + (perf.Value(PdhStandbyNormal) ?? 0) + (perf.Value(PdhStandbyReserve) ?? 0) + (perf.Value(PdhStandbyCore) ?? 0));
                    cachedRamCachedBytes = ramCachedBytes;
                    lastMemoryPerfTime = DateTime.Now;
                }
                double? poolPaged = perf.Value(PdhPoolPaged), poolNonPaged = perf.Value(PdhPoolNonPaged), faults = perf.Value(PdhPageFaults);
                if (poolPaged.HasValue) cachedRamPoolPagedBytes = ramPoolPagedBytes = (long)poolPaged.Value;
                if (poolNonPaged.HasValue) cachedRamPoolNonPagedBytes = ramPoolNonPagedBytes = (long)poolNonPaged.Value;
                if (faults.HasValue) cachedRamActivityVal = ramActivityVal = (long)Math.Round(faults.Value);
            }

            // -1 when the counters cannot be read: the page shows its unavailable state.
            int threadsCount = -1;
            int processesCount = -1;
            double cpuFreqGhz = 0;
            if (perfSampled)
            {
                double? threads = perf.Value(PdhThreads), processes = perf.Value(PdhProcesses);
                if (threads.HasValue && processes.HasValue)
                {
                    threadsCount = (int)threads.Value;
                    processesCount = (int)processes.Value;
                }

                // Real average core frequency. The performance percentage is relative to the
                // counter's own Processor Frequency base (true base clock on hybrid CPUs), not
                // to the WMI MaxClockSpeed value which can be a different reference.
                double? perfPct = perf.Value(PdhCpuPerformance), counterBaseMhz = perf.Value(PdhCpuFrequency);
                if (perfPct > 0 && counterBaseMhz > 0)
                {
                    cpuFreqGhz = counterBaseMhz.Value * perfPct.Value / 100.0 / 1000.0;
                }
            }

            // System drive activity (PDH LogicalDisk, formerly a ~20 ms raw WMI query per tick).
            double? diskRead = null, diskWrite = null, diskIdle = null, diskLatency = null;
            if (perfSampled && diskCountersAvailable)
            {
                diskRead = perf.Value(PdhDiskRead); diskWrite = perf.Value(PdhDiskWrite);
                diskIdle = perf.Value(PdhDiskIdle); diskLatency = perf.Value(PdhDiskLatency);
            }
            bool diskRatesValid = diskRead.HasValue && diskWrite.HasValue;
            double diskReadMbNum = diskRatesValid ? Math.Max(0, diskRead.Value) / (1024.0 * 1024.0) : -1;
            double diskWriteMbNum = diskRatesValid ? Math.Max(0, diskWrite.Value) / (1024.0 * 1024.0) : -1;
            long diskActiveTime = diskIdle.HasValue ? DiskActiveTime(diskIdle.Value) : -1;
            double diskResponse = diskLatency.HasValue ? Math.Max(0, diskLatency.Value) : -1;

            // GPU stats
            int igpuUtil = cachedIgpuUtil;
            long igpuMemBytes = cachedIgpuMemBytes;
            int igpuDecodeUtil = cachedIgpuDecodeUtil;
            int igpuEncodeUtil = cachedIgpuEncodeUtil;
            int npuUtil = cachedNpuUtil;
            long npuMemBytes = cachedNpuMemBytes;
            int dgpuUtil = cachedDgpuUtil;
            if (perfSampled)
            {
                lastGpuAttempt = DateTime.UtcNow;
                gpuCountersFresh = false;
                // The WMI lookup costs 0.3 to 3 s: at most once a minute while no LUID is known.
                if (NpuDetected && string.IsNullOrEmpty(npuLuid) && (DateTime.UtcNow - lastNpuDiscovery).TotalSeconds >= 60)
                {
                    lastNpuDiscovery = DateTime.UtcNow;
                    npuLuid = DiscoverNpuLuid();
                }
                // Once a minute (about 0.1 ms): an adapter registered late, a new LUID after a driver restart or update,
                // a GPU enabled or plugged in. Driver details follow a changed adapter.
                if ((DateTime.UtcNow - lastAdapterDiscovery).TotalSeconds >= 60 && DiscoverDirectXAdapters()) ReadVideoControllers();
                var counters = WindowsGpuTelemetry.Read(perf, igpuLuid, dgpuLuid, npuLuid);
                cachedIgpuUtil = igpuUtil = counters[0].Utilization;
                cachedIgpuDecodeUtil = igpuDecodeUtil = counters[0].Decode;
                cachedIgpuEncodeUtil = igpuEncodeUtil = counters[0].Encode;
                // Same scope as the budget (dedicated carve-out + shared), as Task Manager's "GPU memory".
                cachedIgpuMemBytes = igpuMemBytes = AdapterMemoryBytes(counters[0]);
                cachedDgpuUtil = dgpuUtil = counters[1].Utilization;
                cachedDgpuMemBytes = counters[1].DedicatedBytes;
                cachedNpuUtil = npuUtil = counters[2].Utilization;
                cachedNpuTopPid = counters[2].TopPid;
                cachedIgpuTopPid = counters[0].TopPid; cachedIgpuTopPct = counters[0].TopPct;
                cachedDgpuTopPid = counters[1].TopPid; cachedDgpuTopPct = counters[1].TopPct;
                cachedNpuTopPct = counters[2].TopPct;
                cachedNpuMemBytes = npuMemBytes = counters[2].SharedBytes;
                gpuCountersFresh = counters.Any(c => c.Utilization >= 0);
                if (gpuCountersFresh) lastGpuPerfTime = DateTime.UtcNow;
            }

            // Real battery state (laptops); flag 128 means no system battery.
            bool batteryPresent = false;
            int batteryPercent = -1;
            bool acLine = true;
            // Windows energy saver (battery saver), also available on desktops since Windows 11 24H2.
            bool energySaver = false;
            try
            {
                if (Win32.GetSystemPowerStatus(out SYSTEM_POWER_STATUS sps))
                {
                    energySaver = sps.SystemStatusFlag == 1;
                    batteryPresent = (sps.BatteryFlag & 128) == 0 && sps.BatteryFlag != 255;
                    acLine = sps.ACLineStatus == 1;
                    batteryPercent = sps.BatteryLifePercent <= 100 ? sps.BatteryLifePercent : -1;
                    if (batteryPercent < 0)
                    {
                        batteryPresent = false;
                    }
                }
            }
            catch {}

            // A runtime-suspended discrete GPU (Optimus/hybrid laptops) is never polled: sensors
            // would wake it. Its last reading turns stale, then unavailable.
            bool dgpuSuspended = DevicePower.IsSuspended(dgpuInstanceId);
            bool saveEnergy = !acLine || energySaver;

            // NVIDIA metrics come from NVML in process (no helper process every 5 s); the reads
            // are throttled and skipped while the GPU is suspended.
            NvidiaSample nv = nvidiaTelemetry.Read(GpuName, dgpuSuspended, saveEnergy);
            bool nvidiaStatsSuccess = nv != null;
            int gpuUtil = nv != null && nv.Utilization.HasValue ? (int)Math.Round(nv.Utilization.Value) : dgpuUtil;
            double gpuTemp = nv != null ? nv.Temperature ?? -1 : -1;
            double gpuCoreClock = nv != null ? nv.CoreClock ?? -1 : -1;
            double gpuMemClock = nv != null ? nv.MemoryClock ?? -1 : -1;
            double gpuPowerW = nv != null && nv.PowerWatts.HasValue ? Math.Round(nv.PowerWatts.Value, 1) : -1;
            int gpuFanRpm = -1;
            string gpuSensorStatus = nvidiaTelemetry.Status;
            long gpuSensorAgeMs = nvidiaTelemetry.AgeMs;
            string gpuSensorProvider = NvidiaTelemetry.Provider;

            // AMD and Intel discrete GPUs (or NVIDIA without NVML): driver-reported WDDM
            // sensors. Only what the driver reports; watts are not exposed there (the driver gives
            // a share of its power budget), so the power slot stays unavailable.
            if (!nvidiaTelemetry.IsAvailable(GpuName) && dgpuLuidValue != 0)
            {
                gpuSensorProvider = "wddm";
                long nowMs = collectClock.ElapsedMilliseconds;
                if (!dgpuSuspended && (wddmLastAttemptMs < 0 || nowMs - wddmLastAttemptMs >= 2000))
                {
                    wddmLastAttemptMs = nowMs;
                    wddmSample = WddmGpuSensors.Read(dgpuLuidValue);
                    if (wddmSample != null) wddmLastSuccessMs = nowMs;
                }
                bool wddmFresh = wddmSample != null && wddmLastSuccessMs >= 0 && nowMs - wddmLastSuccessMs <= 6000;
                gpuSensorStatus = wddmFresh ? "fresh" : wddmLastSuccessMs < 0 ? "unavailable" : "stale";
                gpuSensorAgeMs = wddmLastSuccessMs < 0 ? -1 : nowMs - wddmLastSuccessMs;
                if (wddmFresh)
                {
                    gpuTemp = wddmSample.TemperatureC ?? -1;
                    gpuMemClock = wddmSample.MemoryClockMhz ?? -1;
                    gpuFanRpm = wddmSample.FanRpm ?? -1;
                }
            }
            ulong vramTotal = nv != null && nv.TotalMiB.HasValue ? (ulong)(nv.TotalMiB.Value * 1048576) : (ulong)Math.Max(0, dgpuVramTotalBytes);
            ulong vramUsed = nv != null && nv.UsedMiB.HasValue ? (ulong)(nv.UsedMiB.Value * 1048576) : (ulong)Math.Max(0, cachedDgpuMemBytes);
            bool windowsGpuFresh = gpuCountersFresh && (DateTime.UtcNow - lastGpuPerfTime).TotalSeconds <= 6;
            bool vramFresh = (nv != null && nv.UsedMiB.HasValue) || (windowsGpuFresh && cachedDgpuMemBytes >= 0);
            if (!windowsGpuFresh) {
                igpuUtil = npuUtil = igpuDecodeUtil = igpuEncodeUtil = -1;
                if (nv == null || !nv.Utilization.HasValue) gpuUtil = -1;
            }

            UpdatePing();
            UpdateTopProcesses();

            bool igpuDetected = !string.IsNullOrEmpty(igpuLuid) || !string.IsNullOrEmpty(IgpuInfo);
            bool dgpuDetected = nvidiaStatsSuccess || !string.IsNullOrEmpty(dgpuLuid) || !string.IsNullOrEmpty(GpuName);

            int wifiSignal = network.WifiSignal();

            // Plans added, removed or renamed since startup (powercfg, Settings, a driver) show up
            // without restarting; powrprof only, never a powercfg process every few seconds.
            if ((DateTime.UtcNow - lastPowerPlansRefresh).TotalSeconds >= PowerPlansRefreshSeconds)
            {
                lastPowerPlansRefresh = DateTime.UtcNow;
                UpdatePowerPlansCache(false);
            }
            List<PowerPlanInfo> powerPlans = PowerPlans;
            RefreshActivePowerPlan(powerPlans);

            // battery: charge/discharge power (W, + charging / - discharging) and Windows' remaining-time estimate.
            double? batteryRateW = null;
            int batteryMinutes = -1;
            if (batteryPresent)
            {
                BatteryState state;
                if (BatteryState.TryRead(out state))
                {
                    if (state.RateMilliwatts != int.MinValue && state.RateMilliwatts != 0)
                        batteryRateW = Math.Round(state.RateMilliwatts / 1000.0, 1);
                    if (!acLine && state.EstimatedSeconds > 0 && state.EstimatedSeconds < 7 * 24 * 3600)
                        batteryMinutes = (int)(state.EstimatedSeconds / 60);
                }
            }

            // igpu: memory budget registered by the driver (dedicated carve-out + shared system
            // memory, as in Task Manager); Windows sets the shared part to half of the RAM, which
            // is the fallback when the registry value is missing.
            long igpuTotalMb = igpuMemoryBudgetBytes > 0 ? igpuMemoryBudgetBytes / (1024 * 1024) : (long)(ramTotalPhys / 1024 / 1024 / 2);
            long npuTotalMb = (long)(ramTotalPhys / 1024 / 1024 / 2);
            int vramPct = vramTotal > 0 ? (int)Math.Round(Math.Min(100.0, (double)vramUsed / vramTotal * 100)) : 0;
            // CPU: real frequency from the performance counter; empty when it cannot be read.
            string cpuFreqGhzStr = cpuFreqGhz > 0 ? cpuFreqGhz.ToString("F2", CultureInfo.InvariantCulture) : "";
            string windowsGpuStatus = windowsGpuFresh ? "fresh" : lastGpuPerfTime == DateTime.MinValue ? "unavailable" : "stale";
            double sampledAt = Math.Round((DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds);
            ProcessUsage[] topProcesses = cachedTopProcesses;
            ProcessUsage topRamProcess = cachedTopRamProcess;
            int cpuCount = Math.Max(1, Environment.ProcessorCount);

            // Field names and types are the contract read by telemetry-state.js, dashboard.js and app.js.
            return JsonText.Write(json =>
            {
                json.WriteStartObject();
                json.WriteNumber("sampledAt", sampledAt);
                // "nvidia" is the discrete GPU sensor source read by the page, whatever its provider.
                json.WriteStartObject("sources");
                json.WriteStartObject("nvidia");
                json.WriteString("status", gpuSensorStatus);
                json.WriteNumber("ageMs", gpuSensorAgeMs);
                json.WriteString("provider", gpuSensorProvider);
                json.WriteEndObject();
                json.WriteStartObject("windowsGpu");
                json.WriteString("status", windowsGpuStatus);
                json.WriteEndObject();
                json.WriteStartObject("cpuTemperature");
                json.WriteString("status", cpuTemperatureStatus);
                json.WriteString("label", "Zone thermique Windows");
                json.WriteEndObject();
                json.WriteEndObject();

                json.WriteStartObject("cpu");
                json.WriteNumber("utilization", cpuLoad);
                json.WriteNumber("temp", cpuTemp);
                json.WriteString("name", CpuInfo ?? "");
                json.WriteString("freqGhz", cpuFreqGhzStr);
                json.WriteString("baseSpeedGhz", CpuBaseSpeed ?? "");
                json.WriteNumber("cores", CpuCores);
                json.WriteNumber("logical", CpuLogical);
                json.WriteNumber("threads", threadsCount);
                json.WriteString("l2Cache", CpuL2Cache ?? "");
                json.WriteString("l3Cache", CpuL3Cache ?? "");
                json.WriteEndObject();

                json.WriteStartObject("igpu");
                json.WriteBoolean("detected", igpuDetected);
                json.WriteNumber("utilization", igpuUtil);
                json.WriteNumber("decodeUtil", igpuDecodeUtil);
                json.WriteNumber("usedMb", windowsGpuFresh && igpuMemBytes >= 0 ? igpuMemBytes / (1024 * 1024) : -1);
                json.WriteNumber("totalMb", igpuTotalMb);
                json.WriteNumber("totalGb", RoundGb(igpuTotalMb));
                json.WriteString("name", IgpuInfo ?? "");
                json.WriteString("driver", IgpuDriver ?? "");
                json.WriteString("driverDate", IgpuDriverDate ?? "");
                WriteTopProcess(json, igpuDetected, windowsGpuFresh, cachedIgpuTopPid, cachedIgpuTopPct);
                json.WriteNumber("encodeUtil", igpuEncodeUtil);
                json.WriteEndObject();

                json.WriteStartObject("npu");
                json.WriteBoolean("detected", NpuDetected);
                json.WriteNumber("utilization", npuUtil);
                json.WriteNumber("usedMb", windowsGpuFresh && npuMemBytes >= 0 ? npuMemBytes / (1024 * 1024) : -1);
                json.WriteNumber("totalMb", npuTotalMb);
                json.WriteNumber("totalGb", RoundGb(npuTotalMb));
                json.WriteString("name", NpuName ?? "");
                WriteTopProcess(json, NpuDetected, windowsGpuFresh, cachedNpuTopPid, cachedNpuTopPct);
                json.WriteEndObject();

                json.WriteStartObject("vram");
                json.WriteNumber("utilization", vramFresh ? vramPct : -1);
                json.WriteNumber("usedMb", vramFresh ? (long)(vramUsed / (1024 * 1024)) : -1);
                json.WriteNumber("totalMb", vramTotal / (1024 * 1024));
                json.WriteNumber("totalGb", RoundGb((long)(vramTotal / (1024 * 1024))));
                json.WriteEndObject();

                json.WriteStartObject("gpu");
                json.WriteBoolean("detected", dgpuDetected);
                json.WriteNumber("utilization", gpuUtil);
                json.WriteNumber("temp", Finite(gpuTemp));
                json.WriteNumber("coreClock", Finite(gpuCoreClock));
                json.WriteNumber("memoryClock", Finite(gpuMemClock));
                json.WriteNumber("powerW", Finite(gpuPowerW));
                json.WriteString("name", GpuName ?? "");
                json.WriteString("driver", DgpuDriver ?? "");
                json.WriteString("driverDate", DgpuDriverDate ?? "");
                json.WriteNumber("fanRpm", gpuFanRpm);
                WriteTopProcess(json, dgpuDetected, windowsGpuFresh, cachedDgpuTopPid, cachedDgpuTopPct);
                json.WriteEndObject();

                json.WriteStartObject("ram");
                json.WriteNumber("utilization", ramLoad);
                json.WriteNumber("totalGb", TotalRamGb);
                // Task Manager "In use" and "Available" (usable memory, not the installed modules).
                json.WriteString("usedGb", ramTotalPhys > 0 ? Gigabytes((long)(ramTotalPhys - ramFreePhys)) : "");
                json.WriteString("availableGb", ramTotalPhys > 0 ? Gigabytes((long)ramFreePhys) : "");
                json.WriteString("commitUsedGb", Gigabytes(commitUsedBytes));
                json.WriteString("commitLimitGb", Gigabytes(commitLimitBytes));
                json.WriteString("cachedGb", Gigabytes(ramCachedBytes));
                json.WriteNumber("poolPagedMb", ramPoolPagedBytes / (1024 * 1024));
                json.WriteNumber("poolNonPagedMb", ramPoolNonPagedBytes / (1024 * 1024));
                json.WriteNumber("speedMts", RamSpeedMts);
                json.WriteNumber("activity", ramActivityVal);
                json.WriteString("type", RamType ?? "");
                json.WriteNumber("modules", RamModules);
                json.WriteEndObject();

                // Unavailable disk readings are the number -1, never a placeholder throughput.
                json.WriteStartObject("disk");
                json.WriteNumber("freeGb", Math.Round(diskFreeGb, 1));
                json.WriteNumber("utilization", diskActiveTime);
                json.WriteNumber("storagePercent", diskStoragePercent);
                json.WriteNumber("totalGb", (int)diskTotalGb);
                WriteRate(json, "readMb", diskReadMbNum);
                WriteRate(json, "writeMb", diskWriteMbNum);
                json.WriteNumber("responseTimeMs", diskResponse >= 0 && double.IsFinite(diskResponse) ? Math.Round(diskResponse * 1000.0, 1) : -1);
                json.WriteString("name", DiskName ?? "");
                json.WriteString("drive", SystemDrive);
                json.WriteEndObject();

                json.WriteStartObject("network");
                json.WriteNumber("lan", network.TxKBps);
                json.WriteNumber("wifi", network.RxKBps);
                json.WriteString("name", network.Name ?? "");
                json.WriteString("type", network.Type ?? "");
                json.WriteNumber("linkSpeedMbps", network.LinkSpeedMbps);
                json.WriteNumber("signal", wifiSignal);
                json.WriteEndObject();

                json.WriteStartObject("battery");
                json.WriteBoolean("present", batteryPresent);
                json.WriteNumber("percent", batteryPercent);
                json.WriteBoolean("ac", acLine);
                if (batteryRateW.HasValue) json.WriteNumber("rateW", batteryRateW.Value); else json.WriteNull("rateW");
                json.WriteNumber("minutesLeft", batteryMinutes);
                json.WriteEndObject();
                json.WriteBoolean("energySaver", energySaver);

                // Real Windows uptime, not the process lifetime.
                json.WriteNumber("uptime", Win32.GetTickCount64() / 1000);
                json.WriteNumber("ping", pingTime);
                json.WriteString("pingVia", pingViaGateway ? "gateway" : "internet");
                json.WriteNumber("totalProcesses", processesCount);
                json.WriteString("motherboard", MotherboardInfo ?? "");

                json.WriteStartArray("topProcesses");
                foreach (var process in topProcesses)
                {
                    json.WriteStartObject();
                    json.WriteString("Name", process.Name ?? "");
                    json.WriteNumber("PercentProcessorTime", process.CpuPercent * cpuCount);
                    json.WriteNumber("WorkingSetPrivate", process.RamMb * 1024L * 1024L);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteStartObject("topRamProcess");
                if (topRamProcess != null)
                {
                    json.WriteString("name", topRamProcess.Name ?? "");
                    json.WriteNumber("ramMb", topRamProcess.RamMb);
                }
                json.WriteEndObject();

                json.WriteStartArray("powerPlans");
                foreach (var plan in powerPlans)
                {
                    json.WriteStartObject();
                    json.WriteString("guid", plan.Guid ?? "");
                    json.WriteString("name", plan.Name ?? "");
                    json.WriteBoolean("active", plan.Active);
                    json.WriteString("kind", plan.Kind ?? "");
                    json.WriteString("mode", plan.Overlay ? "overlay" : "plan");
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            });
        }

        // A non-finite double would make the whole message invalid JSON: report it as unavailable.
        private static double Finite(double value) { return double.IsFinite(value) ? value : -1; }
        // Task Manager "Active time": the share of the interval the disk was not idle.
        internal static long DiskActiveTime(double idlePercent) { return (long)Math.Round(100.0 - Math.Max(0.0, Math.Min(100.0, idlePercent))); }
        // Dedicated + shared usage of an adapter; -1 when neither counter was read.
        internal static long AdapterMemoryBytes(WindowsGpuSample sample)
        {
            return sample.SharedBytes < 0 && sample.DedicatedBytes < 0 ? -1 : Math.Max(0, sample.SharedBytes) + Math.Max(0, sample.DedicatedBytes);
        }
        // Binary gigabytes rounded like the page does ("16 Go" for 16376 MiB), never truncated.
        private static long RoundGb(long megabytes) { return (long)Math.Round(megabytes / 1024.0); }
        private static string Gigabytes(long bytes) { return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("F1", CultureInfo.InvariantCulture); }
        // Throughput as a one-decimal string, or the number -1 when unavailable.
        private static void WriteRate(Utf8JsonWriter json, string name, double megabytes)
        {
            if (megabytes >= 0 && double.IsFinite(megabytes)) json.WriteString(name, megabytes.ToString("F1", CultureInfo.InvariantCulture));
            else json.WriteNumber(name, -1);
        }
    }
}
