using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Xamarin.Essentials;

namespace YIRSHospital.Services
{
    public class UserSession
    {
        public string FullName { get; set; }
        public string Email { get; set; }
        public string Category { get; set; }
        public string CollectionPoint { get; set; }
        public DateTime ExpiresAt { get; set; }

        [JsonIgnore]
        public bool IsValid => !string.IsNullOrWhiteSpace(Email)
                               && DateTime.UtcNow < ExpiresAt;
    }

    public static class SessionService
    {
        private const string SESSION_KEY = "user_session_v1";
        // 4 months
        private static readonly TimeSpan SESSION_LIFETIME = TimeSpan.FromDays(120);

        // ── Save ──────────────────────────────────────────────────────────────

        public static async Task SaveAsync(string fullName, string email,
            string category, string collectionPoint)
        {
            try
            {
                var session = new UserSession
                {
                    FullName = fullName,
                    Email = email,
                    Category = category,
                    CollectionPoint = collectionPoint,
                    ExpiresAt = DateTime.UtcNow.Add(SESSION_LIFETIME)
                };

                string json = JsonConvert.SerializeObject(session);
                // SecureStorage preferred; falls back to Preferences if unavailable
                try
                {
                    await SecureStorage.SetAsync(SESSION_KEY, json);
                }
                catch
                {
                    Preferences.Set(SESSION_KEY, json);
                }

                Debug.WriteLine($"[Session] Saved. Expires: {session.ExpiresAt:u}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Session] SaveAsync error: {ex.Message}");
            }
        }

        // ── Load ──────────────────────────────────────────────────────────────

        public static async Task<UserSession> LoadAsync()
        {
            try
            {
                string json = null;

                try { json = await SecureStorage.GetAsync(SESSION_KEY); }
                catch { }

                if (string.IsNullOrWhiteSpace(json))
                    json = Preferences.Get(SESSION_KEY, null);

                if (string.IsNullOrWhiteSpace(json)) return null;

                var session = JsonConvert.DeserializeObject<UserSession>(json);
                Debug.WriteLine($"[Session] Loaded. Valid={session?.IsValid}, Expires={session?.ExpiresAt:u}");
                return session;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Session] LoadAsync error: {ex.Message}");
                return null;
            }
        }

        // ── Clear ─────────────────────────────────────────────────────────────

        public static void Clear()
        {
            try { SecureStorage.Remove(SESSION_KEY); } catch { }
            try { Preferences.Remove(SESSION_KEY); } catch { }
            Debug.WriteLine("[Session] Cleared.");
        }
    }
}