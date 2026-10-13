using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopHtmlHost
{
    // One button of the power panel: a Windows power plan, or a Windows 10/11 power mode overlay
    // (the "Power mode" setting of Modern Standby laptops that ship a single plan).
    public class PowerPlanInfo
    {
        public string Guid;
        public string Name;
        public bool Active;
        public string Kind = "balanced"; // eco, balanced or performance: icon and order, never the label
        public bool Overlay;
    }

    // Power plans through powrprof (Unicode names, no child process, no output parsing).
    internal static class PowerSchemes
    {
        internal const string SaverPlan = "a1841308-3541-4fab-bc81-f71556f20b4a";
        internal const string BalancedPlan = "381b4222-f694-41f0-9685-ff5bb260df2e";
        internal const string HighPerformancePlan = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        internal const string UltimatePlan = "e9a42b02-d5df-448d-aa00-03f14749eb61";
        internal const string OverlayEfficiency = "961cc777-2547-4f9d-8174-7d86181b8a7a";
        internal const string OverlayBalanced = "00000000-0000-0000-0000-000000000000";
        internal const string OverlayPerformance = "ded574b5-45a0-4f42-8737-46345c09c238";

        const uint ACCESS_SCHEME = 16;
        // NO_SUBGROUP_GUID and GUID_POWERSCHEME_PERSONALITY (passed by reference to powrprof).
        static Guid NoSubgroup = new Guid("fea3413e-7e05-4911-9a71-700331f1c294");
        static Guid Personality = new Guid("245d8541-3943-4422-b025-13a784f679b7");

        [DllImport("powrprof.dll")]
        static extern uint PowerEnumerate(IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subGroupGuid, uint accessFlags, uint index, byte[] buffer, ref uint bufferSize);
        [DllImport("powrprof.dll")]
        static extern uint PowerReadFriendlyName(IntPtr rootPowerKey, ref Guid schemeGuid, IntPtr subGroupGuid, IntPtr settingGuid, byte[] buffer, ref uint bufferSize);
        [DllImport("powrprof.dll")]
        static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid settingGuid, out uint value);
        [DllImport("powrprof.dll")]
        static extern uint PowerGetEffectiveOverlayScheme(out Guid overlayGuid);
        [DllImport("powrprof.dll")]
        static extern uint PowerSetActiveOverlayScheme(ref Guid overlayGuid);

        internal static string KindFromGuid(string guid)
        {
            switch ((guid ?? "").ToLowerInvariant())
            {
                case SaverPlan: case OverlayEfficiency: return "eco";
                case BalancedPlan: case OverlayBalanced: return "balanced";
                case HighPerformancePlan: case UltimatePlan: case OverlayPerformance: return "performance";
                default: return null;
            }
        }

        // GUID_POWERSCHEME_PERSONALITY: the built-in plan a custom plan was derived from.
        internal static string KindFromPersonality(uint personality)
        {
            switch (personality)
            {
                case 0: return "performance";
                case 1: return "eco";
                case 2: return "balanced";
                default: return null;
            }
        }

        // Explicit words of the plan name (accents removed, whole words only, so "Recommended"
        // is not "eco"); null when the name says nothing.
        internal static string KindFromName(string name)
        {
            string decomposed = (name ?? "").Normalize(NormalizationForm.FormD);
            var plain = new StringBuilder(decomposed.Length);
            foreach (char c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                plain.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
            }
            var words = new HashSet<string>(plain.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            string[] eco = { "economie", "economies", "eco", "saver", "saving", "silent", "silencieux", "quiet", "battery", "batterie", "efficiency", "efficacite" };
            string[] performance = { "performance", "performances", "performant", "turbo", "extreme", "extremes", "gaming", "jeu", "jeux", "ultimate", "optimales", "max" };
            if (eco.Any(words.Contains)) return "eco";
            if (performance.Any(words.Contains)) return "performance";
            string[] balanced = { "balanced", "equilibre", "normal", "normale", "recommended", "recommande", "standard", "auto" };
            return balanced.Any(words.Contains) ? "balanced" : null;
        }

        internal static int KindOrder(string kind)
        {
            return kind == "eco" ? 0 : kind == "balanced" ? 1 : kind == "performance" ? 2 : 3;
        }

        internal static List<PowerPlanInfo> Sort(IEnumerable<PowerPlanInfo> plans)
        {
            return plans.OrderBy(p => KindOrder(p.Kind)).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(p => p.Guid, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Every installed plan, or null when powrprof cannot enumerate them.
        internal static List<PowerPlanInfo> EnumeratePlans()
        {
            try
            {
                var plans = new List<PowerPlanInfo>();
                for (uint index = 0; index < 64; index++)
                {
                    byte[] guidBytes = new byte[16];
                    uint size = (uint)guidBytes.Length;
                    if (PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ACCESS_SCHEME, index, guidBytes, ref size) != 0) break;
                    var scheme = new Guid(guidBytes);
                    string name = ReadFriendlyName(scheme);
                    string guid = scheme.ToString();
                    // OEM plans often keep the Balanced personality whatever their purpose (MSI
                    // "Performances extremes" here), so an explicit name wins over it.
                    uint personality;
                    string kind = KindFromGuid(guid) ?? KindFromName(name);
                    if (kind == null && PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref NoSubgroup, ref Personality, out personality) == 0)
                        kind = KindFromPersonality(personality);
                    plans.Add(new PowerPlanInfo { Guid = guid, Name = string.IsNullOrEmpty(name) ? guid : name, Kind = kind ?? "balanced" });
                }
                return plans.Count > 0 ? plans : null;
            }
            catch (Exception ex)
            {
                Program.LogDebug("PowerEnumerate failed: " + ex.Message);
                return null;
            }
        }

        static string ReadFriendlyName(Guid scheme)
        {
            uint size = 0;
            PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, null, ref size);
            if (size == 0 || size > 4096) return "";
            byte[] buffer = new byte[size];
            if (PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref size) != 0) return "";
            return Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0').Trim();
        }

        // Undocumented but stable since Windows 10 1709 (Settings uses it); null when missing.
        internal static string EffectiveOverlay()
        {
            try
            {
                Guid overlay;
                return PowerGetEffectiveOverlayScheme(out overlay) == 0 ? overlay.ToString() : null;
            }
            catch (Exception) { return null; }
        }

        internal static uint SetOverlay(string guid)
        {
            Guid overlay = new Guid(guid);
            try { return PowerSetActiveOverlayScheme(ref overlay); }
            catch (Exception) { return uint.MaxValue; }
        }

        // The three Windows power modes, labelled like the Windows Settings page.
        internal static List<PowerPlanInfo> OverlayModes(string activeOverlay, CultureInfo culture)
        {
            bool french = culture != null && culture.TwoLetterISOLanguageName == "fr";
            var modes = new List<PowerPlanInfo>
            {
                new PowerPlanInfo { Guid = OverlayEfficiency, Kind = "eco", Overlay = true, Name = french ? "Meilleure efficacité énergétique" : "Best power efficiency" },
                new PowerPlanInfo { Guid = OverlayBalanced, Kind = "balanced", Overlay = true, Name = french ? "Équilibré" : "Balanced" },
                new PowerPlanInfo { Guid = OverlayPerformance, Kind = "performance", Overlay = true, Name = french ? "Meilleures performances" : "Best performance" }
            };
            foreach (var mode in modes) mode.Active = mode.Guid.Equals(activeOverlay, StringComparison.OrdinalIgnoreCase);
            return modes;
        }
    }
}
