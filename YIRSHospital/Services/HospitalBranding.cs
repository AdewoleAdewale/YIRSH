using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace YIRSHospital.Services
{
    /// <summary>
    /// Everything that changes on a printed receipt when the agent's hospital
    /// changes: logo asset, official name, contact line and watermark.
    ///
    /// Call sites should never hard-code "Logo.png" or a hospital name again —
    /// they read HospitalBranding.Current, which follows HospitalContext.Code.
    /// </summary>
    public sealed class HospitalBranding
    {
        /// <summary>Hospital code this branding belongs to.</summary>
        public string Code { get; private set; }

        /// <summary>
        /// Printed as the receipt header. Used only when the live hospital name
        /// from GetHospitalInfo is unavailable — see <see cref="StoreName"/>.
        /// </summary>
        public string FallbackName { get; private set; }

        /// <summary>Short form, used for the diagonal watermark.</summary>
        public string ShortName { get; private set; }

        /// <summary>Filename inside the Android Assets folder.</summary>
        public string LogoAsset { get; private set; }

        public string Phone { get; private set; }

        private HospitalBranding(string code, string fallbackName, string shortName,
                                 string logoAsset, string phone)
        {
            Code = code;
            FallbackName = fallbackName;
            ShortName = shortName;
            LogoAsset = logoAsset;
            Phone = phone;
        }

        // ── Registry ──────────────────────────────────────────────────────────

        private static readonly HospitalBranding Default = new HospitalBranding(
            "DEFAULT",
            "YOBE STATE HOSPITALS MANAGEMENT BOARD",
            "YOBE HEALTH",
            "Logo.png",
            "Contact: +234 907 070 1616");

        private static readonly Dictionary<string, HospitalBranding> _registry =
            new Dictionary<string, HospitalBranding>(StringComparer.OrdinalIgnoreCase)
            {
                { "DEFAULT", Default },

                { "DAMAGUM", new HospitalBranding(
                    "DAMAGUM",
                    "GENERAL HOSPITAL DAMAGUM",
                    "DAMAGUM",
                    "Logo.png",
                    "Contact: +234 907 070 1616") },

                { "POTISKUM", new HospitalBranding(
                    "POTISKUM",
                    "STATE SPECIALIST HOSPITAL POTISKUM",
                    "POTISKUM",
                    "YSSHP.png",
                    "Contact: +234 907 070 1616") }
            };

        /// <summary>Branding for the hospital the agent is currently logged in as.</summary>
        public static HospitalBranding Current
        {
            get { return For(HospitalContext.Code); }
        }

        public static HospitalBranding For(string hospitalCode)
        {
            if (string.IsNullOrWhiteSpace(hospitalCode)) return Default;

            HospitalBranding branding;
            if (_registry.TryGetValue(hospitalCode.Trim(), out branding)) return branding;

            Debug.WriteLine("[Branding] No branding for '" + hospitalCode + "' — using default.");
            return Default;
        }

        // ── Receipt values ────────────────────────────────────────────────────

        /// <summary>
        /// The name printed on the receipt. Prefers the live displayName confirmed
        /// by GetHospitalInfo so the receipt always matches what the platform has
        /// on record, and falls back to the table above if that call hasn't run.
        ///
        /// DEFAULT is the exception: its API displayName is literally "DEFAULT",
        /// which is meaningless on a printed receipt, so the fallback wins there.
        /// </summary>
        public string StoreName
        {
            get
            {
                if (string.Equals(Code, "DEFAULT", StringComparison.OrdinalIgnoreCase))
                    return FallbackName;

                var live = HospitalContext.DisplayName;

                if (!string.IsNullOrWhiteSpace(live)
                    && !string.Equals(live, HospitalContext.Code, StringComparison.OrdinalIgnoreCase))
                    return live.ToUpperInvariant();

                return FallbackName;
            }
        }

        public string WatermarkText
        {
            get { return ShortName; }
        }

        /// <summary>
        /// Returns <see cref="LogoAsset"/> if it exists in the Assets folder,
        /// otherwise the shared default logo. Without this a missing asset prints
        /// a receipt with no logo at all, which looks like a printer fault.
        /// </summary>
        public string ResolveLogoAsset()
        {
            if (AssetExists(LogoAsset)) return LogoAsset;

            Debug.WriteLine("[Branding] Asset '" + LogoAsset + "' missing — falling back to " + Default.LogoAsset);
            return AssetExists(Default.LogoAsset) ? Default.LogoAsset : null;
        }

        private static bool AssetExists(string assetName)
        {
            if (string.IsNullOrWhiteSpace(assetName)) return false;

            try
            {
                var context = Android.App.Application.Context;
                using (var stream = context.Assets.Open(assetName))
                    return stream != null;
            }
            catch
            {
                return false;
            }
        }
    }
}