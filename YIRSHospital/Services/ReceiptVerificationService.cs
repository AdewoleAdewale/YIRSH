using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using YIRSHospital.Services;

namespace YIRSH.Services
{
    public class VerifyResult
    {
        public bool Ok { get; set; }
        public VerifyTransactionResponse Data { get; set; }
        public string Error { get; set; }
    }

    /// <summary>
    /// Calls /Api/Agents/VerifyTransaction. The HttpClient MUST be the process-wide
    /// singleton (ApiClient) so the TLS trust fix applies.
    /// </summary>
    public class ReceiptVerificationService
    {
        private const string Endpoint = "https://yobe.osoftpay.net/Api/Agents/VerifyTransaction";
        private readonly HttpClient _http;

        public ReceiptVerificationService(HttpClient http)
        {
            if (http == null) throw new ArgumentNullException("http");
            _http = http;
        }

        public async Task<VerifyResult> VerifyAsync(string transactionId)
        {
            transactionId = (transactionId ?? string.Empty).Trim();
            if (transactionId.Length == 0)
                return new VerifyResult { Error = "Enter a transaction ID." };

            try
            {
                string url = Endpoint + "?transactionId=" + Uri.EscapeDataString(transactionId);
                HttpResponseMessage resp = await _http.GetAsync(url).ConfigureAwait(false);

                // Some endpoints on this API return 405 to GET; retry once as form POST.
                if (resp.StatusCode == HttpStatusCode.MethodNotAllowed)
                {
                    var form = new FormUrlEncodedContent(new List<KeyValuePair<string, string>>
                    {
                        new KeyValuePair<string, string>("transactionId", transactionId)
                    });
                    resp = await _http.PostAsync(Endpoint, form).ConfigureAwait(false);
                }

                string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!resp.IsSuccessStatusCode)
                    return new VerifyResult { Error = "Server returned " + (int)resp.StatusCode + ". Please try again." };

                var data = JsonConvert.DeserializeObject<VerifyTransactionResponse>(body);
                if (data == null)
                    return new VerifyResult { Error = "Empty response from server." };

                if (!data.IsSuccess)
                    return new VerifyResult { Data = data, Error = string.IsNullOrEmpty(data.Message) ? "Transaction could not be verified." : data.Message };

                return new VerifyResult { Ok = true, Data = data };
            }
            catch (JsonException)
            {
                return new VerifyResult { Error = "Unexpected response format from server." };
            }
            catch (Exception ex)
            {
                // Cert-chain failures surface here on Android; message distinguishes them from offline.
                bool trust = ex.ToString().IndexOf("Trust anchor", StringComparison.OrdinalIgnoreCase) >= 0;
                return new VerifyResult
                {
                    Error = trust
                        ? "Secure connection could not be established (certificate issue)."
                        : "Network error. Check your connection and try again."
                };
            }
        }
    }
}