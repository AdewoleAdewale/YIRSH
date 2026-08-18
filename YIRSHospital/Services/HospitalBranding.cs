using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace YIRSHospital.Services
{
  
    public sealed class HospitalBranding
    {
        /// <summary>Hospital code this branding belongs to.</summary>
        public string Code { get; private set; }

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
            "YOBE STATE SPECIALIST HOSPITALS ",
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
                    " SPECIALIST HOSPITAL POTISKUM",
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