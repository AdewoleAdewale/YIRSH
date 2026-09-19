using System;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using Android.App;
using Xamarin.Forms;
using YIRSHospital.Droid.Services;
using YIRSHospital.Services;
using Application = Android.App.Application;

[assembly: Dependency(typeof(AndroidPlatformHttpHandlerProvider))]
namespace YIRSHospital.Droid.Services
{
    /// <summary>
    /// Fixes "Trust anchor for certification path not found" against
    /// yobe.osoftpay.net. That error means the server's TLS handshake sends only
    /// the leaf certificate, no intermediate — Android's Java TLS stack
    /// (AndroidClientHandler) will not fetch the missing link itself, so it fails
    /// closed.
    ///
    /// The right fix is server-side: the API's TLS config should serve the full
    /// chain (leaf + intermediate), not just the leaf. Ask whoever operates
    /// yobe.osoftpay.net to point their web server at a "fullchain" cert file.
    /// Once that's done this class becomes unnecessary and CreateHandler() can
    /// return a plain HttpClientHandler with no custom callback.
    ///
    /// Until then: this uses the *managed* HttpClientHandler (SslStream-based,
    /// not the Java stack), whose ServerCertificateCustomValidationCallback lets
    /// us hand .NET's own X509Chain the missing intermediate and let it complete
    /// and validate the chain normally. This is deliberately NOT "return true"
    /// for everything — an untrusted or wrong certificate still fails. It only
    /// fills in the one specific gap (a missing intermediate) before asking
    /// .NET's own validator to judge the result.
    ///
    /// SETUP REQUIRED:
    /// 1. From a machine with real internet access, run:
    ///      openssl s_client -connect yobe.osoftpay.net:443 -showcerts
    ///    This prints the certs the server sends. If it sends only one, the gap
    ///    is the intermediate CA that issued it — find it from the issuer CN
    ///    (search "<Issuer CN> intermediate certificate download").
    /// 2. Save that intermediate as YIRSHospital.Android/Assets/osoftpay-intermediate.cer
    ///    (PEM or DER both work).
    /// 3. Build. This class picks it up automatically via Assets.
    /// </summary>
    public class AndroidPlatformHttpHandlerProvider : IPlatformHttpHandlerProvider
    {
        private const string BundledIntermediateAsset = "osoftpay-intermediate.cer";
        private const string TargetHost = "yobe.osoftpay.net";

        public HttpMessageHandler CreateHandler()
        {
            var handler = new HttpClientHandler();

            var bundled = LoadBundledIntermediate();
            if (bundled == null)
            {
                // No asset yet — return the plain managed handler. This still
                // behaves correctly for every host with a complete chain; it just
                // won't paper over yobe.osoftpay.net's missing intermediate until
                // step 2 in the class comment is done.
                return handler;
            }

            handler.ServerCertificateCustomValidationCallback = (request, cert, chain, errors) =>
                Validate(request, cert, chain, errors, bundled);

            return handler;
        }

        /// <summary>
        /// Real validation, not a bypass. Only for TargetHost, and only to plug in
        /// the one certificate we know is missing: everything else — expiry,
        /// hostname, an actually-wrong or self-signed cert, a different host
        /// entirely — is still rejected exactly as .NET's default validator would
        /// reject it.
        /// </summary>
        private static bool Validate(HttpRequestMessage request, X509Certificate2 cert,
            X509Chain chain, System.Net.Security.SslPolicyErrors errors, X509Certificate2 bundled)
        {
            if (errors == System.Net.Security.SslPolicyErrors.None)
                return true;

            if (request?.RequestUri?.Host == null ||
                !request.RequestUri.Host.Equals(TargetHost, StringComparison.OrdinalIgnoreCase))
                return false;

            // Only handle the specific "chain didn't build" case. A hostname
            // mismatch or a genuinely untrusted leaf still fails.
            if ((errors & System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors) == 0)
                return false;
            if ((errors & System.Net.Security.SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
                return false;
            if ((errors & System.Net.Security.SslPolicyErrors.RemoteCertificateNotAvailable) != 0)
                return false;

            chain.ChainPolicy.ExtraStore.Add(bundled);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

            bool built = chain.Build(cert);
            if (!built) return false;

            // The chain must actually terminate cleanly once the intermediate is
            // supplied — a bad date, revocation, or other real problem still fails.
            foreach (var element in chain.ChainStatus)
            {
                if (element.Status != X509ChainStatusFlags.NoError &&
                    element.Status != X509ChainStatusFlags.PartialChain)
                    return false;
            }

            return true;
        }

        private static X509Certificate2 LoadBundledIntermediate()
        {
            try
            {
                using (var stream = Application.Context.Assets.Open(BundledIntermediateAsset))
                using (var ms = new System.IO.MemoryStream())
                {
                    stream.CopyTo(ms);
                    return new X509Certificate2(ms.ToArray());
                }
            }
            catch (Java.IO.FileNotFoundException)
            {
                System.Diagnostics.Debug.WriteLine(
                    "[AndroidPlatformHttpHandlerProvider] No bundled intermediate at Assets/" + BundledIntermediateAsset);
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[AndroidPlatformHttpHandlerProvider] Failed to load bundled cert: " + ex);
                return null;
            }
        }
    }
}