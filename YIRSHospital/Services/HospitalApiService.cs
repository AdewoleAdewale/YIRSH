using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using YIRSHospital.Models;
using YIRSHospital.Views;

namespace YIRSHospital.Services
{
    #region DTOs

    public class HospitalInfo
    {
        public string code { get; set; }
        public string displayName { get; set; }

        // Convenience for Picker binding
        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(displayName) ? code : displayName;
        }
    }

    public class HospitalDepartment
    {
        public string name { get; set; }
        public int id { get; set; }
    }

    /// <summary>Priced service, from the legacy ListRevServices catalogue.</summary>
    public class ServiceCatalogItem
    {
        public string serviceName { get; set; }
        public decimal amount { get; set; }
    }

    public class DepartmentServicesResponse
    {
        [JsonProperty("message")]
        public string Message { get; set; }
        [JsonProperty("code")]
        public string Code { get; set; }
        [JsonProperty("services")]
        public List<DepartmentServiceItem> Services { get; set; } = new List<DepartmentServiceItem>();
    }

    public class DepartmentServiceItem
    {
        [JsonProperty("serviceName")]
        public string ServiceName { get; set; }

        [JsonProperty("amount")]
        public decimal Amount { get; set; }

        [JsonIgnore]
        public bool IsSelected { get; set; } // Used for UI binding
    }
    public class PatientRegistration
    {
        public string FullName { get; set; }
        public string PatentNo { get; set; }
        public string AgentName { get; set; }
        public string PhoneNumber { get; set; }
        public string Address { get; set; }
        public string Email { get; set; }
        public string Gender { get; set; }
        public string Age { get; set; }
        public string MaritalStatus { get; set; }
        public string GuarantorName { get; set; }
        public string Relationship { get; set; }
        public string QuarantorPhone { get; set; }   // spelling matches the API
        public string HospitalCode { get; set; }
    }

    public class PatientRegistrationResult
    {
        public string message { get; set; }
        public string code { get; set; }
        public string patientId { get; set; }
        public RegisteredPatient patient { get; set; }

        [JsonIgnore]
        public bool IsSuccess { get { return code == "00"; } }
    }

    public class RegisteredPatient
    {
        public int id { get; set; }
        public string fullName { get; set; }
        public int hospitalId { get; set; }
        public string patentNo { get; set; }
        public string agentName { get; set; }
        public string phoneNumber { get; set; }
        public string address { get; set; }
        public string email { get; set; }
        public string gender { get; set; }
        public string age { get; set; }
        public string maritalStatus { get; set; }
        public string guarantorName { get; set; }
        public string relationship { get; set; }
        public string quarantorPhone { get; set; }
    }

    public class HospitalPaymentService
    {
        public string ServiceName { get; set; }
        public int Quantity { get; set; }
        public decimal Amount { get; set; }
    }

    public class HospitalPaymentRequest
    {
        public string hospitalCode { get; set; }
        public string department { get; set; }
        public string HospitalNo { get; set; }
        public string email { get; set; }
        public string pin { get; set; }
        public string paymentMethod { get; set; }
        public List<HospitalPaymentService> Services { get; set; }
    }

    public class RaiseBillRequest
    {
        [JsonProperty("PatientNo")]
        public string PatientNo { get; set; }

        [JsonProperty("HospitalCode")]
        public string HospitalCode { get; set; }

        [JsonProperty("Department")]
        public string Department { get; set; }

        [JsonProperty("Email")]
        public string Email { get; set; }

        [JsonProperty("Notes")]
        public string Notes { get; set; }

        [JsonProperty("Services")]
        public List<RaiseBillServiceItem> Services { get; set; } = new List<RaiseBillServiceItem>();
    }

    public class RaiseBillServiceItem
    {
        [JsonProperty("ServiceName")]
        public string ServiceName { get; set; }

        [JsonProperty("Amount")]
        public decimal Amount { get; set; }
    }

    public class RaiseBillResponse
    {
        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("code")]
        public string Code { get; set; }

        [JsonProperty("billGroupId")]
        public string BillGroupId { get; set; }

        [JsonProperty("patientNo")]
        public string PatientNo { get; set; }

        [JsonProperty("patientName")]
        public string PatientName { get; set; }

        [JsonProperty("department")]
        public string Department { get; set; }

        [JsonProperty("grandTotal")]
        public decimal GrandTotal { get; set; }

        [JsonProperty("totalServices")]
        public int TotalServices { get; set; }

        [JsonProperty("services")]
        public List<RaisedServiceItem> Services { get; set; } = new List<RaisedServiceItem>();

        [JsonProperty("raisedBy")]
        public string RaisedBy { get; set; }

        [JsonProperty("dateRaised")]
        public string DateRaised { get; set; }
    }

    public class RaisedServiceItem
    {
        [JsonProperty("serviceName")]
        public string ServiceName { get; set; }

        [JsonProperty("amount")]
        public decimal Amount { get; set; }
    }

    // ══════════════════════════════════════════════════════════════════
    // 4. GET PATIENT BILL CONTRACTS
    // ══════════════════════════════════════════════════════════════════
    public class PatientBillResponse
    {
        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("code")]
        public string Code { get; set; }

        [JsonProperty("patientName")]
        public string PatientName { get; set; }

        [JsonProperty("patientNo")]
        public string PatientNo { get; set; }

        [JsonProperty("gender")]
        public string Gender { get; set; }

        [JsonProperty("phoneNumber")]
        public string PhoneNumber { get; set; }

        [JsonProperty("hospitalName")]
        public string HospitalName { get; set; }

        [JsonProperty("hospitalCode")]
        public string HospitalCode { get; set; }

        [JsonProperty("billGroupId")]
        public string BillGroupId { get; set; }

        [JsonProperty("department")]
        public string Department { get; set; }

        [JsonProperty("grandTotal")]
        public decimal GrandTotal { get; set; }

        [JsonProperty("totalServices")]
        public int TotalServices { get; set; }

        [JsonProperty("services")]
        public List<PendingBillServiceItem> Services { get; set; } = new List<PendingBillServiceItem>();
    }

    public class PendingBillServiceItem
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("serviceName")]
        public string ServiceName { get; set; }

        [JsonProperty("amount")]
        public decimal Amount { get; set; }

        [JsonProperty("notes")]
        public string Notes { get; set; }

        [JsonProperty("raisedByName")]
        public string RaisedByName { get; set; }

        [JsonProperty("dateRaised")]
        public string DateRaised { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }
    }

    // ══════════════════════════════════════════════════════════════════
    // 5. PROCESS PATIENT BILL CONTRACTS
    // ══════════════════════════════════════════════════════════════════
    public class ProcessBillRequest
    {
        [JsonProperty("HospitalCode")]
        public string HospitalCode { get; set; }

        [JsonProperty("HospitalNo")]
        public string HospitalNo { get; set; }

        [JsonProperty("Department")]
        public string Department { get; set; }

        [JsonProperty("Email")]
        public string Email { get; set; }

        [JsonProperty("Pin")]
        public string Pin { get; set; }

        [JsonProperty("MerchantNo")]
        public string MerchantNo { get; set; }

        [JsonProperty("PaymentMethod")]
        public string PaymentMethod { get; set; }

        [JsonProperty("PaymentReference")]
        public string PaymentReference { get; set; }

        [JsonProperty("Services")]
        public List<ProcessBillServiceItem> Services { get; set; } = new List<ProcessBillServiceItem>();
    }

    public class ProcessBillServiceItem
    {
        [JsonProperty("ServiceName")]
        public string ServiceName { get; set; }

        [JsonProperty("Quantity")]
        public int Quantity { get; set; } = 1;

        [JsonProperty("Amount")]
        public decimal Amount { get; set; }
    }

    public class ProcessBillResponse
    {
        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("code")]
        public string Code { get; set; }

        [JsonProperty("patientName")]
        public string PatientName { get; set; }

        [JsonProperty("patientNo")]
        public string PatientNo { get; set; }

        [JsonProperty("billGroupId")]
        public string BillGroupId { get; set; }

        [JsonProperty("transactionNo")]
        public string TransactionNo { get; set; }

        [JsonProperty("department")]
        public string Department { get; set; }

        [JsonProperty("totalAmount")]
        public decimal TotalAmount { get; set; }

        [JsonProperty("breakdowns")]
        public List<BillBreakdownItem> Breakdowns { get; set; } = new List<BillBreakdownItem>();

        [JsonProperty("paymentMethod")]
        public string PaymentMethod { get; set; }

        [JsonProperty("paymentReference")]
        public string PaymentReference { get; set; }

        [JsonProperty("debitRef")]
        public string DebitRef { get; set; }
    }

    public class BillBreakdownItem
    {
        [JsonProperty("serviceName")]
        public string ServiceName { get; set; }

        [JsonProperty("amount")]
        public decimal Amount { get; set; }

        [JsonProperty("quantity")]
        public int Quantity { get; set; }

        [JsonProperty("subTotal")]
        public decimal SubTotal { get; set; }
    }

    // ══════════════════════════════════════════════════════════════════
    // 6. CONFIRM PATIENT PAYMENT CONTRACTS
    // ══════════════════════════════════════════════════════════════════
    public class ConfirmPaymentResponse
    {
        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("code")]
        public string Code { get; set; }

        [JsonProperty("isRecent")]
        public bool IsRecent { get; set; }

        [JsonProperty("paymentStatus")]
        public string PaymentStatus { get; set; } // "PAID TODAY" or "PAID PREVIOUSLY"

        [JsonProperty("patientName")]
        public string PatientName { get; set; }

        [JsonProperty("patientNo")]
        public string PatientNo { get; set; }

        [JsonProperty("gender")]
        public string Gender { get; set; }

        [JsonProperty("phoneNumber")]
        public string PhoneNumber { get; set; }

        [JsonProperty("department")]
        public string Department { get; set; }

        [JsonProperty("transactionId")]
        public string TransactionId { get; set; }

        [JsonProperty("serviceName")]
        public string ServiceName { get; set; }

        [JsonProperty("amount")]
        public decimal Amount { get; set; }

        [JsonProperty("paymentMethod")]
        public string PaymentMethod { get; set; }

        [JsonProperty("date")]
        public string Date { get; set; }

        [JsonProperty("revenueHead")]
        public string RevenueHead { get; set; }

        [JsonProperty("cashedBy")]
        public string CashedBy { get; set; }

        [JsonProperty("debitRef")]
        public string DebitRef { get; set; }
    }
    public class HospitalPaymentBreakdown
    {
        public string serviceName { get; set; }
        public decimal amount { get; set; }
        public int quantity { get; set; }
        public decimal subTotal { get; set; }
    }

    public class HospitalPaymentResult
    {
        public string respondCode { get; set; }
        public string transactionNo { get; set; }
        public string department { get; set; }
        public string message { get; set; }
        public string status { get; set; }
        public string responseMessage { get; set; }
        public string noofDaysPaid { get; set; }
        public string expDate { get; set; }
        public string payerId { get; set; }
        public decimal totalAmount { get; set; }
        public List<HospitalPaymentBreakdown> breakdown { get; set; }
        public string vehicleNo { get; set; }

        [JsonIgnore]
        public bool IsSuccess { get { return respondCode == "00"; } }
    }

    public class HospitalPaymentHistoryItem
    {
        public string department { get; set; }
        public string serviceName { get; set; }
        public string transactionId { get; set; }
        public string amount { get; set; }
        public string dateRecorded { get; set; }

        [JsonIgnore]
        public decimal AmountValue
        {
            get
            {
                decimal parsed;
                return decimal.TryParse(amount, NumberStyles.Any, CultureInfo.InvariantCulture, out parsed)
                    ? parsed : 0m;
            }
        }

        /// <summary>
        /// The docs say dateRecorded is MM/DD/YY but the live sample ("06/08/26 03:52 PM"
        /// for 6 Aug 2026) is dd/MM/yy. We try day-first, then month-first, then fall back.
        /// </summary>
        [JsonIgnore]
        public DateTime? RecordedAt
        {
            get
            {
                if (string.IsNullOrWhiteSpace(dateRecorded)) return null;

                var formats = new[]
                {
                    "dd/MM/yy hh:mm tt", "MM/dd/yy hh:mm tt",
                    "dd/MM/yyyy hh:mm tt", "MM/dd/yyyy hh:mm tt",
                    "dd/MM/yy HH:mm", "MM/dd/yy HH:mm"
                };

                DateTime parsed;
                if (DateTime.TryParseExact(dateRecorded.Trim(), formats, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out parsed))
                    return parsed;

                if (DateTime.TryParse(dateRecorded, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out parsed))
                    return parsed;

                return null;
            }
        }
    }

    public class ApiResult<T>
    {
        public bool Success { get; set; }
        public T Data { get; set; }
        public string ErrorMessage { get; set; }

        public static ApiResult<T> Ok(T data)
        {
            return new ApiResult<T> { Success = true, Data = data };
        }

        public static ApiResult<T> Fail(string message)
        {
            return new ApiResult<T> { Success = false, ErrorMessage = message };
        }
    }

    #endregion

    /// <summary>
    /// Single entry point for every hospital-scoped call.
    /// Nothing else in the app should build these URLs by hand.
    /// </summary>
    public static class HospitalApiService
    {
        public const string ROOT = "https://yobe.osoftpay.net";
        private const string AGENTS = ROOT + "/Api/Agents";

        private static readonly HttpClient _client = CreateClient();

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (m, cert, chain, errors) => true,
                SslProtocols = System.Security.Authentication.SslProtocols.Tls12
                             | System.Security.Authentication.SslProtocols.Tls11
            };

            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        }

        // ── 1. Hospital list ──────────────────────────────────────────────────

        public static async Task<ApiResult<List<HospitalInfo>>> GetHospitalListAsync(
            CancellationToken ct = default(CancellationToken))
        {
            return await GetJsonAsync<List<HospitalInfo>>(AGENTS + "/HospitalList", ct);
        }

        // ── 2. Hospital code list ─────────────────────────────────────────────

        public static async Task<ApiResult<List<string>>> GetHospitalCodeListAsync(
            CancellationToken ct = default(CancellationToken))
        {
            return await GetJsonAsync<List<string>>(AGENTS + "/HospitalCodeList", ct);
        }

        // ── 3. Hospital info ──────────────────────────────────────────────────

        public static async Task<ApiResult<HospitalInfo>> GetHospitalInfoAsync(
            string hospitalCode, CancellationToken ct = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(hospitalCode))
                return ApiResult<HospitalInfo>.Fail("No hospital selected.");

            var url = AGENTS + "/GetHospitalInfo?HospitalCode=" + Uri.EscapeDataString(hospitalCode);
            return await GetJsonAsync<HospitalInfo>(url, ct);
        }

        // ── 4. Departments for a hospital ─────────────────────────────────────

        /// <summary>
        /// GET /AllHospitalDepartment?HospitalCode=X.
        /// A GET against this route currently answers 405 on the live host, so we
        /// retry as a form POST before giving up. Remove the fallback once the
        /// backend settles on one verb.
        /// </summary>
        public static async Task<ApiResult<List<HospitalDepartment>>> GetDepartmentsAsync(
            string hospitalCode, CancellationToken ct = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(hospitalCode))  return ApiResult<List<HospitalDepartment>>.Fail("No hospital selected.");

            string code = string.IsNullOrWhiteSpace(hospitalCode) ? HospitalContext.Code : hospitalCode;
            bool isDefault = string.Equals(code, "DEFAULT", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(code);
            string url = isDefault
                  ? AGENTS + "/ListDepartment"
                  : AGENTS + "/AllHospitalDepartment?HospitalCode=" + Uri.EscapeDataString(code);

            Debug.WriteLine("[HospitalApi] GetDepartments -> GET " + url);
             var result = await GetJsonAsync<List<HospitalDepartment>>(url, ct);

            if (result.Success && result.Data != null && result.Data.Count > 0)
                return result;

            Debug.WriteLine("[HospitalApi] Department GET failed (" + result.ErrorMessage + "), retrying as POST.");

        

            if (!isDefault)
            {
                Debug.WriteLine("[HospitalApi] Specific department GET failed, falling back to ListDepartment");
                return await GetJsonAsync<List<HospitalDepartment>>(AGENTS + "/ListDepartment", ct);
            }

            return result;
        }

        // ── 5. Register patient ───────────────────────────────────────────────

        public static async Task<ApiResult<PatientRegistrationResult>> RegisterPatientAsync(
            PatientRegistration data, CancellationToken ct = default(CancellationToken))
        {
            if (data == null)
                return ApiResult<PatientRegistrationResult>.Fail("Nothing to register.");
            if (string.IsNullOrWhiteSpace(data.HospitalCode))
                return ApiResult<PatientRegistrationResult>.Fail("No hospital selected.");

            try
            {
                using (var form = new MultipartFormDataContent())
                {
                    Add(form, "FullName", data.FullName);
                    Add(form, "PatentNo", data.PatentNo);
                    Add(form, "AgentName", data.AgentName);
                    Add(form, "PhoneNumber", data.PhoneNumber);
                    Add(form, "Address", data.Address);
                    Add(form, "Email", data.Email);
                    Add(form, "Gender", data.Gender);
                    Add(form, "Age", data.Age);
                    Add(form, "MaritalStatus", data.MaritalStatus);
                    Add(form, "GuarantorName", data.GuarantorName);
                    Add(form, "Relationship", data.Relationship);
                    Add(form, "QuarantorPhone", data.QuarantorPhone);
                    Add(form, "HospitalCode", data.HospitalCode);

                    using (var response = await _client.PostAsync(AGENTS + "/PatientReg", form, ct))
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        Debug.WriteLine("[HospitalApi] PatientReg -> " + response.StatusCode + ": " + Trim(json));

                        if (!response.IsSuccessStatusCode)
                            return ApiResult<PatientRegistrationResult>.Fail(
                                DescribeStatus(response.StatusCode, json));

                        var parsed = JsonConvert.DeserializeObject<PatientRegistrationResult>(json);
                        if (parsed == null)
                            return ApiResult<PatientRegistrationResult>.Fail("Empty response from server.");

                        return parsed.IsSuccess
                            ? ApiResult<PatientRegistrationResult>.Ok(parsed)
                            : ApiResult<PatientRegistrationResult>.Fail(parsed.message ?? "Registration failed.");
                    }
                }
            }
            catch (Exception ex)
            {
                return ApiResult<PatientRegistrationResult>.Fail(Describe(ex));
            }
        }

        private static void Add(MultipartFormDataContent form, string name, string value)
        {
            form.Add(new StringContent(value ?? string.Empty, Encoding.UTF8), name);
        }

        // ── 6. Hospital payment ───────────────────────────────────────────────

        public static async Task<ApiResult<HospitalPaymentResult>> MakePaymentAsync(
            HospitalPaymentRequest request, CancellationToken ct = default(CancellationToken))
        {
            if (request == null)
                return ApiResult<HospitalPaymentResult>.Fail("Nothing to pay for.");
            if (string.IsNullOrWhiteSpace(request.hospitalCode))
                return ApiResult<HospitalPaymentResult>.Fail("No hospital selected.");
            if (request.Services == null || request.Services.Count == 0)
                return ApiResult<HospitalPaymentResult>.Fail("Select at least one service.");

            try
            {
                var payload = JsonConvert.SerializeObject(request);
                Debug.WriteLine("[HospitalApi] HospitalPayment <- " + payload);

                using (var content = new StringContent(payload, Encoding.UTF8, "application/json"))
                using (var response = await _client.PostAsync(AGENTS + "/HospitalPayment", content, ct))
                {
                    var json = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine("[HospitalApi] HospitalPayment -> " + response.StatusCode + ": " + Trim(json));

                    if (!response.IsSuccessStatusCode)
                        return ApiResult<HospitalPaymentResult>.Fail(DescribeStatus(response.StatusCode, json));

                    var parsed = JsonConvert.DeserializeObject<HospitalPaymentResult>(json);
                    if (parsed == null)
                        return ApiResult<HospitalPaymentResult>.Fail("Empty response from server.");

                    return parsed.IsSuccess
                        ? ApiResult<HospitalPaymentResult>.Ok(parsed)
                        : ApiResult<HospitalPaymentResult>.Fail(
                            parsed.message ?? parsed.responseMessage ?? "Payment declined.");
                }
            }
            catch (Exception ex)
            {
                return ApiResult<HospitalPaymentResult>.Fail(Describe(ex));
            }
        }

        // ── 7. Payment history ────────────────────────────────────────────────

        public static async Task<ApiResult<List<HospitalPaymentHistoryItem>>> GetPaymentHistoryAsync(
            string email, DateTime from, DateTime to, string hospitalCode,
            CancellationToken ct = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(hospitalCode))
                return ApiResult<List<HospitalPaymentHistoryItem>>.Fail("No hospital selected.");

            var url = AGENTS + "/AllHospitalPaymentHistory"
                    + "?Email=" + Uri.EscapeDataString(email ?? string.Empty)
                    + "&SearchFrom=" + from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    + "&SearchTo=" + to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    + "&HospitalCode=" + Uri.EscapeDataString(hospitalCode);

            return await GetJsonAsync<List<HospitalPaymentHistoryItem>>(url, ct);
        }

        // ── Service catalogue (legacy priced list) ────────────────────────────



        /// <summary>
        /// Works out which RevHead string returns a priced catalogue for the current
        /// hospital, trying the agent's collection point, the hospital display name,
        /// then the bare code. The winner is cached per hospital so this runs once.
        /// </summary>
        public static async Task<string> ResolveRevenueHeadAsync(
            string hospitalCode, string displayName, string agentCollectionPoint,
            string probeDepartment, CancellationToken ct = default(CancellationToken))
        {
            if (!string.IsNullOrWhiteSpace(HospitalContext.RevenueHead))
                return HospitalContext.RevenueHead;

            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(agentCollectionPoint)) candidates.Add(agentCollectionPoint);
            if (!string.IsNullOrWhiteSpace(displayName)) candidates.Add(displayName);
            if (!string.IsNullOrWhiteSpace(hospitalCode)) candidates.Add(hospitalCode);

            foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var probe = await GetDepartmentServicesAsync(candidate, probeDepartment, ct);
                if (probe.Success && probe.Data != null && probe.Data.Services.Count > 0)
                    {
                    Debug.WriteLine("[HospitalApi] RevHead for " + hospitalCode + " resolved to '" + candidate + "'.");
                    HospitalContext.CacheRevenueHead(candidate);
                    return candidate;
                }
            }

            // Nothing matched — fall back to the collection point so behaviour is unchanged.
            Debug.WriteLine("[HospitalApi] RevHead for " + hospitalCode + " unresolved; using collection point.");
            return agentCollectionPoint;
        }

        // ── Plumbing ──────────────────────────────────────────────────────────

        private static readonly Lazy<HttpClient> _insecureClient = new Lazy<HttpClient>(() =>
        {
            var handler = new HttpClientHandler
            {
                // Bypasses Java SSL/TLS handshake & trust anchor validation errors
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true,
                SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls11
            };

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        });

        private static HttpClient Client => _insecureClient.Value;

        public static async Task<ApiResult<PatientTransactionResponse>> GetPatientTransactionsAsync(
            string patientNo, string hospitalCode = null, int? hospitalId = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(patientNo))
                return ApiResult<PatientTransactionResponse>.Fail("Patient Number is required.");

            string code = string.IsNullOrWhiteSpace(hospitalCode) ? HospitalContext.Code : hospitalCode;
            bool isDefault = string.Equals(code, "DEFAULT", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(code);

            string url;
            if (isDefault)
            {
                // Specialist Hospital (DEFAULT) uses patientId
                url = $"{AGENTS}/GetPatientTransactions?patientId={Uri.EscapeDataString(patientNo)}";
            }
            else
            {
                // Specific hospitals (POTISKUM, DAMAGUM, etc.)
                int resolvedId = hospitalId ?? ResolveHospitalId(code);
                url = $"{AGENTS}/GetHospitalPatientTransactions?patientNo={Uri.EscapeDataString(patientNo)}&hospitalId={resolvedId}&hospitalCode={Uri.EscapeDataString(code)}";
            }

            Debug.WriteLine($"[HospitalApi] GetPatientTransactions -> GET {url}");

            try
            {
                using (var response = await Client.GetAsync(url, ct))
                {
                    var json = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                        return ApiResult<PatientTransactionResponse>.Fail($"Server error ({response.StatusCode})");

                    var data = JsonConvert.DeserializeObject<PatientTransactionResponse>(json);
                    return ApiResult<PatientTransactionResponse>.Ok(data);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HospitalApi] SSL/Network Exception: {ex.Message}");
                return ApiResult<PatientTransactionResponse>.Fail(ex.Message);
            }
        }

        private static int ResolveHospitalId(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return 1;

            string clean = code.Trim().ToUpperInvariant();

            if (clean.Contains("POTISKUM") || clean.Contains("PORTISKUM"))
                return 2;

            if (clean.Contains("DAMAGUM") || clean.Contains("DAMAGUN"))
                return 1;

            return 1;
        }


        private static async Task<ApiResult<T>> GetJsonAsync<T>(string url, CancellationToken ct)
        {
            try
            {
                Debug.WriteLine("[HospitalApi] GET " + url);

                using (var response = await _client.GetAsync(url, ct))
                {
                    var json = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                        return ApiResult<T>.Fail(DescribeStatus(response.StatusCode, json));

                    if (string.IsNullOrWhiteSpace(json))
                        return ApiResult<T>.Fail("Empty response from server.");

                    return ApiResult<T>.Ok(JsonConvert.DeserializeObject<T>(json));
                }
            }
            catch (Exception ex)
            {
                return ApiResult<T>.Fail(Describe(ex));
            }
        }

        private static string DescribeStatus(System.Net.HttpStatusCode status, string body)
        {
            switch (status)
            {
                case System.Net.HttpStatusCode.NotFound:
                    return "That hospital or record was not found.";
                case System.Net.HttpStatusCode.Unauthorized:
                case System.Net.HttpStatusCode.Forbidden:
                    return "Your session is no longer valid. Please log in again.";
                case System.Net.HttpStatusCode.BadRequest:
                    return string.IsNullOrWhiteSpace(body)
                        ? "The server rejected the request."
                        : Trim(body);
                case System.Net.HttpStatusCode.MethodNotAllowed:
                    return "Endpoint does not accept this request type (405).";
                default:
                    return "Server error (" + (int)status + "). Please try again.";
            }
        }

        private static string Describe(Exception ex)
        {
            if (ex is TaskCanceledException)
                return "The request timed out. Check your connection and try again.";
            if (ex is HttpRequestException)
                return "Network error. Check your connection and try again.";
            if (ex is JsonException)
                return "The server sent data the app could not read.";

            Debug.WriteLine("[HospitalApi] " + ex);
            return "Something went wrong. Please try again.";
        }

        private static string Trim(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= 400 ? value : value.Substring(0, 400) + "…";
        }

        // ── 3. Raise Patient Bill ──────────────────────────────────────────
        public static async Task<ApiResult<RaiseBillResponse>> RaisePatientBillAsync(RaiseBillRequest payload, CancellationToken ct = default)
        {
            string url = $"{AGENTS}/RaisePatientBill";
            return await PostJsonAsync<RaiseBillResponse>(url, payload, ct);
        }

        // ── 4. Get Patient Bill ────────────────────────────────────────────
        public static async Task<ApiResult<PatientBillResponse>> GetPatientBillAsync(string patientNo, string hospitalCode, CancellationToken ct = default)
        {
            string url = $"{AGENTS}/GetPatientBill?patientNo={Uri.EscapeDataString(patientNo)}&hospitalCode={Uri.EscapeDataString(hospitalCode)}";
            return await GetJsonAsync<PatientBillResponse>(url, ct);
        }

        // ── 5. Process Patient Bill ────────────────────────────────────────
        public static async Task<ApiResult<ProcessBillResponse>> ProcessPatientBillAsync(ProcessBillRequest payload, CancellationToken ct = default)
        {
            string url = $"{AGENTS}/ProcessPatientBill";
            return await PostJsonAsync<ProcessBillResponse>(url, payload, ct);
        }

        private static async Task<ApiResult<T>> PostJsonAsync<T>(string url, object payload, CancellationToken ct)
        {
            try
            {
                var jsonPayload = JsonConvert.SerializeObject(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                using (var response = await _client.PostAsync(url, content, ct))
                {
                    var json = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                        return ApiResult<T>.Fail($"Server error ({response.StatusCode}).");

                    if (string.IsNullOrWhiteSpace(json))
                        return ApiResult<T>.Fail("Empty response from server.");

                    var data = JsonConvert.DeserializeObject<T>(json);
                    return ApiResult<T>.Ok(data);
                }
            }
            catch (Exception ex)
            {
                return ApiResult<T>.Fail(ex.Message);
            }
        }

        public static async Task<ApiResult<DepartmentServicesResponse>> GetDepartmentServicesAsync(string hospitalCode, string department, CancellationToken ct = default)
        {
            string url = $"{AGENTS}/GetDepartmentServices?hospitalCode={Uri.EscapeDataString(hospitalCode)}&department={Uri.EscapeDataString(department)}";
            return await GetJsonAsync<DepartmentServicesResponse>(url, ct);
        }
        // ── 6. Confirm Patient Payment ─────────────────────────────────────
        public static async Task<ApiResult<ConfirmPaymentResponse>> ConfirmPatientPaymentAsync(string patientNo, CancellationToken ct = default)
        {
            string url = $"{AGENTS}/ConfirmPatientPayment?patientNo={Uri.EscapeDataString(patientNo)}";
            return await GetJsonAsync<ConfirmPaymentResponse>(url, ct);
        }


        public static async Task<ApiResult<RaisePatientBillResponse>> RaisePatientBillAsync(RaisePatientBillRequest payload, CancellationToken ct = default)
        {
            string url = $"{AGENTS}/RaisePatientBill";
            return await PostJsonAsync<RaisePatientBillResponse>(url, payload, ct);
        }

       

        public static async Task<ApiResult<ProcessPatientBillResponse>> ProcessPatientBillAsync(ProcessPatientBillRequest payload, CancellationToken ct = default)
        {
            string url = $"{AGENTS}/ProcessPatientBill";
            return await PostJsonAsync<ProcessPatientBillResponse>(url, payload, ct);
        }

    }
}