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

        /// <summary>
        /// TEMPORARY — SCOPED TO ONE HOST, NOT A GLOBAL BYPASS.
        /// Set to false the moment yobe.osoftpay.net's TLS config sends its full
        /// certificate chain (leaf + intermediate) instead of just the leaf —
        /// that's the actual bug; this flag is a stopgap, not the fix. While
        /// true, this app will accept ANY certificate presented by that one host,
        /// including one from an attacker on the same network. Every other host
        /// still validates normally.
        /// </summary>
        private const bool BypassCertificateValidationForOsoftpay = true;
        private const string OsoftpayHost = "yobe.osoftpay.net";

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

            if (handler == null)
            {
                var httpClientHandler = new HttpClientHandler
                {
                    SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls11
                };

                if (BypassCertificateValidationForOsoftpay)
                {
                    httpClientHandler.ServerCertificateCustomValidationCallback = (request, cert, chain, errors) =>
                    {
                        if (errors == System.Net.Security.SslPolicyErrors.None) return true;

                        bool isTargetHost = request?.RequestUri?.Host != null &&
                            request.RequestUri.Host.Equals(OsoftpayHost, StringComparison.OrdinalIgnoreCase);

                        if (isTargetHost)
                        {
                            Debug.WriteLine("[ApiClient] Bypassing certificate validation for " + OsoftpayHost
                                + " (errors: " + errors + "). Remove this once the server sends its full chain.");
                            return true;
                        }

                        // Every other host still gets real validation.
                        return false;
                    };
                }

                handler = httpClientHandler;
            }

            var client = new HttpClient(handler, disposeHandler: true);

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

    public interface IPlatformHttpHandlerProvider
    {
        /// <summary>
        /// Returns a fresh handler configured with the platform's TLS stack plus
        /// any additional trust anchors the app ships. Never returns null; if the
        /// platform cannot be configured it must fall back to the stock handler.
        /// </summary>
        HttpMessageHandler CreateHandler();
    }
}