using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Xamarin.Essentials;

namespace YIRSHospital.Services
{
    /// <summary>
    /// Which of the two categories the HospitalLogin response returned:
    ///   "Hospital" -> cashier/agent, uses ProcessPatientBill + ConfirmPatientPayment
    ///   "Billers"  -> department staff, uses RaisePatientBill only, and is locked
    ///                 to one department instead of picking from a list.
    /// </summary>
    public static class LoginCategories
    {
        public const string Hospital = "Hospital";
        public const string Billers = "Billers";

        public static bool IsHospital(string category) =>
            string.Equals((category ?? string.Empty).Trim(), Hospital, StringComparison.OrdinalIgnoreCase);

        public static bool IsBillers(string category) =>
            string.Equals((category ?? string.Empty).Trim(), Billers, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Holds the department a logged-in Billers-category staff member belongs to.
    /// The login response now names the department directly, so RaisePatientBill
    /// no longer needs the staff member to pick one from a dropdown — it locks
    /// to this value instead. Empty/null for a Hospital-category (agent) login.
    /// </summary>
    public static class StaffContext
    {
        private const string STAFF_KEY = "selected_staff_department_v1";

        public static string Department { get; private set; }

        public static bool IsLockedToDepartment => !string.IsNullOrWhiteSpace(Department);

        public static async Task SelectAsync(string department)
        {
            Department = string.IsNullOrWhiteSpace(department) ? null : department.Trim();
            await PersistAsync();
            Debug.WriteLine("[Staff] Locked to department " + (Department ?? "(none)"));
        }

        private class StaffState
        {
            public string Department { get; set; }
        }

        private static async Task PersistAsync()
        {
            try
            {
                var json = JsonConvert.SerializeObject(new StaffState { Department = Department });
                try { await SecureStorage.SetAsync(STAFF_KEY, json); }
                catch { Preferences.Set(STAFF_KEY, json); }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Staff] PersistAsync error: " + ex.Message);
            }
        }

        public static async Task<bool> RestoreAsync()
        {
            try
            {
                string json = null;
                try { json = await SecureStorage.GetAsync(STAFF_KEY); }
                catch { }

                if (string.IsNullOrWhiteSpace(json))
                    json = Preferences.Get(STAFF_KEY, null);

                if (string.IsNullOrWhiteSpace(json)) return false;

                var state = JsonConvert.DeserializeObject<StaffState>(json);
                Department = state?.Department;
                return !string.IsNullOrWhiteSpace(Department);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Staff] RestoreAsync error: " + ex.Message);
                return false;
            }
        }

        public static void Clear()
        {
            Department = null;
            try { SecureStorage.Remove(STAFF_KEY); } catch { }
            try { Preferences.Remove(STAFF_KEY); } catch { }
        }
    }
}