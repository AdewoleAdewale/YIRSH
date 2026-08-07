using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Xamarin.Essentials;

namespace YIRSHospital.Services
{
    /// <summary>
    /// Holds the hospital the agent is currently logged in as.
    /// Every hospital-scoped API call in the app reads HospitalContext.Code.
    ///
    /// Lifecycle:
    ///   LoginPage  -> SelectAsync(code, displayName)  (before AgentLogin)
    ///   Dashboard  -> RefreshInfoAsync()              (API #3, confirms details)
    ///   Logout     -> Clear()
    /// </summary>
    public static class HospitalContext
    {
        private const string HOSPITAL_KEY = "selected_hospital_v1";
        private const string REVHEAD_KEY_PREFIX = "revhead_for_";

        // ── State ─────────────────────────────────────────────────────────────

        /// <summary>Hospital code, e.g. "DEFAULT", "DAMAGUM", "POTISKUM".</summary>
        public static string Code { get; private set; }

        /// <summary>Human-readable name, e.g. "GENERAL HOSPITAL DAMAGUM".</summary>
        public static string DisplayName { get; private set; }

        /// <summary>
        /// The value the legacy /ListRevServices endpoint expects as RevHead for
        /// this hospital. Resolved once per hospital and cached (see
        /// HospitalApiService.ResolveRevenueHeadAsync).
        /// </summary>
        public static string RevenueHead { get; internal set; }

        public static bool IsSelected
        {
            get { return !string.IsNullOrWhiteSpace(Code); }
        }

        /// <summary>True when the agent is on the original single-hospital module.</summary>
        public static bool IsDefaultHospital
        {
            get { return string.Equals(Code, "DEFAULT", StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>Safe label for headers/receipts.</summary>
        public static string Label
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(DisplayName)) return DisplayName;
                if (!string.IsNullOrWhiteSpace(Code)) return Code;
                return "YOBE STATE HOSPITALS";
            }
        }

        // ── Selection ─────────────────────────────────────────────────────────

        public static async Task SelectAsync(string code, string displayName)
        {
            Code = (code ?? string.Empty).Trim().ToUpperInvariant();
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Code : displayName.Trim();
            RevenueHead = Preferences.Get(REVHEAD_KEY_PREFIX + Code, null);

            await PersistAsync();
            Debug.WriteLine("[Hospital] Selected " + Code + " (" + DisplayName + ")");
        }

        public static void CacheRevenueHead(string revHead)
        {
            if (string.IsNullOrWhiteSpace(Code) || string.IsNullOrWhiteSpace(revHead)) return;
            RevenueHead = revHead;
            try { Preferences.Set(REVHEAD_KEY_PREFIX + Code, revHead); } catch { }
        }

        // ── Persistence ───────────────────────────────────────────────────────

        private class HospitalState
        {
            public string Code { get; set; }
            public string DisplayName { get; set; }
            public string RevenueHead { get; set; }
        }

        private static async Task PersistAsync()
        {
            try
            {
                var json = JsonConvert.SerializeObject(new HospitalState
                {
                    Code = Code,
                    DisplayName = DisplayName,
                    RevenueHead = RevenueHead
                });

                try { await SecureStorage.SetAsync(HOSPITAL_KEY, json); }
                catch { Preferences.Set(HOSPITAL_KEY, json); }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Hospital] PersistAsync error: " + ex.Message);
            }
        }

        public static async Task<bool> RestoreAsync()
        {
            try
            {
                string json = null;
                try { json = await SecureStorage.GetAsync(HOSPITAL_KEY); }
                catch { }

                if (string.IsNullOrWhiteSpace(json))
                    json = Preferences.Get(HOSPITAL_KEY, null);

                if (string.IsNullOrWhiteSpace(json)) return false;

                var state = JsonConvert.DeserializeObject<HospitalState>(json);
                if (state == null || string.IsNullOrWhiteSpace(state.Code)) return false;

                Code = state.Code;
                DisplayName = state.DisplayName;
                RevenueHead = state.RevenueHead;

                Debug.WriteLine("[Hospital] Restored " + Code);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Hospital] RestoreAsync error: " + ex.Message);
                return false;
            }
        }

        public static void Clear()
        {
            Code = null;
            DisplayName = null;
            RevenueHead = null;

            try { SecureStorage.Remove(HOSPITAL_KEY); } catch { }
            try { Preferences.Remove(HOSPITAL_KEY); } catch { }
            Debug.WriteLine("[Hospital] Cleared.");
        }
    }
}