using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Authentication;
using Xamarin.Forms;

namespace YIRSHospital.Services
{
    /// <summary>
    /// One HttpClient for the process.
    ///
    /// Two reasons this exists rather than each page doing "new HttpClient()":
    ///
    /// 1. TLS trust. The handler is built once, on the platform head, with the
    ///    app's bundled trust anchors attached. A page that news up its own
    ///    HttpClient silently gets the stock handler and fails the handshake
    ///    against yobe.osoftpay.net.
    /// 2. Socket lifetime. Xamarin pages are created and destroyed on every
    ///    navigation; a per-page HttpClient leaks a connection pool each time.
    /// </summary>
    public static class ApiClient
    {
        private const int DefaultTimeoutSeconds = 30;

        private static readonly Lazy<HttpClient> Instance =
            new Lazy<HttpClient>(Create, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

        public static HttpClient Shared
        {
            get { return Instance.Value; }
        }

        private static HttpClient Create()
        {
            HttpMessageHandler handler = null;

            try
            {
                var provider = DependencyService.Get<IPlatformHttpHandlerProvider>();
                if (provider != null) handler = provider.CreateHandler();
            }
            catch (Exception ex)
            {
                // A missing or broken provider must not take the app down; the
                // stock handler still works for any host with a complete chain.
                Debug.WriteLine("[ApiClient] Platform handler unavailable: " + ex.Message);
            }

            var client = handler != null
                ? new HttpClient(handler, disposeHandler: true)
                : new HttpClient();

            client.Timeout = TimeSpan.FromSeconds(DefaultTimeoutSeconds);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.ExpectContinue = false;

            return client;
        }

        /// <summary>
        /// True when the failure is a certificate/handshake problem rather than a
        /// dropped connection. Worth distinguishing: "no signal" is the agent's
        /// problem, "certificate not trusted" is ours, and the two need different
        /// messages on screen and different escalation paths in the field.
        /// </summary>
        public static bool IsTlsFailure(Exception ex)
        {
            var current = ex;

            while (current != null)
            {
                if (current is AuthenticationException) return true;

                var name = current.GetType().FullName ?? string.Empty;
                if (name.IndexOf("SSL", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("CertPath", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Certificate", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                var message = current.Message ?? string.Empty;
                if (message.IndexOf("Trust anchor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("CertPathValidator", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("SSLHandshake", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("certificate", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                current = current.InnerException;
            }

            return false;
        }
    }
}