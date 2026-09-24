using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Xamarin.Forms;

namespace YIRSHospital.Models
{
    public class RaisePatientBillRequest
    {
        [JsonProperty("PatientNo")] public string PatientNo { get; set; }
        [JsonProperty("HospitalCode")] public string HospitalCode { get; set; }
        [JsonProperty("Department")] public string Department { get; set; }
        [JsonProperty("Email")] public string Email { get; set; }
        [JsonProperty("Notes")] public string Notes { get; set; }
        [JsonProperty("Services")] public List<RaiseServicePayload> Services { get; set; } = new List<RaiseServicePayload>();
    }

    public class RaiseServicePayload
    {
        [JsonProperty("ServiceName")] public string ServiceName { get; set; }
        [JsonProperty("Amount")] public decimal Amount { get; set; }
    }

    public class RaisePatientBillResponse
    {
        [JsonProperty("message")] public string Message { get; set; }
        [JsonProperty("code")] public string Code { get; set; }
        [JsonProperty("billGroupId")] public string BillGroupId { get; set; }
        [JsonProperty("patientNo")] public string PatientNo { get; set; }
        [JsonProperty("patientName")] public string PatientName { get; set; }
        [JsonProperty("department")] public string Department { get; set; }
        [JsonProperty("grandTotal")] public decimal GrandTotal { get; set; }
        [JsonProperty("totalServices")] public int TotalServices { get; set; }
        [JsonProperty("raisedBy")] public string RaisedBy { get; set; }
        [JsonProperty("dateRaised")] public string DateRaised { get; set; }
    }

    public class PendingBillService
    {
        [JsonProperty("id")] public int Id { get; set; }
        [JsonProperty("serviceName")] public string ServiceName { get; set; }
        [JsonProperty("amount")] public decimal Amount { get; set; }
        [JsonProperty("notes")] public string Notes { get; set; }
        [JsonProperty("raisedByName")] public string RaisedByName { get; set; }
        [JsonProperty("dateRaised")] public string DateRaised { get; set; }
        [JsonProperty("status")] public string Status { get; set; }
    }

    public class ProcessPatientBillRequest
    {
        [JsonProperty("HospitalCode")] public string HospitalCode { get; set; }
        [JsonProperty("HospitalNo")] public string HospitalNo { get; set; }
        [JsonProperty("Department")] public string Department { get; set; }
        [JsonProperty("Email")] public string Email { get; set; }
        [JsonProperty("Pin")] public string Pin { get; set; }
        [JsonProperty("MerchantNo")] public string MerchantNo { get; set; }
        [JsonProperty("PaymentMethod")] public string PaymentMethod { get; set; }
        [JsonProperty("PaymentReference")] public string PaymentReference { get; set; }
        [JsonProperty("Services")] public List<ProcessServicePayload> Services { get; set; } = new List<ProcessServicePayload>();
    }

    public class ProcessServicePayload
    {
        [JsonProperty("ServiceName")] public string ServiceName { get; set; }
        [JsonProperty("Quantity")] public int Quantity { get; set; } = 1;
        [JsonProperty("Amount")] public decimal Amount { get; set; }
    }

    public class ProcessPatientBillResponse
    {
        [JsonProperty("message")] public string Message { get; set; }
        [JsonProperty("code")] public string Code { get; set; }
        [JsonProperty("transactionNo")] public string TransactionNo { get; set; }
        [JsonProperty("totalAmount")] public decimal TotalAmount { get; set; }
        [JsonProperty("debitRef")] public string DebitRef { get; set; }
    }

    public class ConfirmPaymentResponse
    {
        [JsonProperty("message")] public string Message { get; set; }
        [JsonProperty("code")] public string Code { get; set; }
        [JsonProperty("isRecent")] public bool IsRecent { get; set; }
        [JsonProperty("paymentStatus")] public string PaymentStatus { get; set; }
        [JsonProperty("patientName")] public string PatientName { get; set; }
        [JsonProperty("patientNo")] public string PatientNo { get; set; }
        [JsonProperty("department")] public string Department { get; set; }
        [JsonProperty("transactionId")] public string TransactionId { get; set; }
        [JsonProperty("serviceName")] public string ServiceName { get; set; }
        [JsonProperty("amount")] public decimal Amount { get; set; }
        [JsonProperty("paymentMethod")] public string PaymentMethod { get; set; }
        [JsonProperty("date")] public string Date { get; set; }
        [JsonProperty("cashedBy")] public string CashedBy { get; set; }
    }

    public class RecentBillTransaction
    {
        [JsonProperty("payer")]
        public string payer { get; set; }

        [JsonProperty("amount")]
        public decimal AmountValue { get; set; }

        [JsonProperty("dateRecorded")]
        public string dateRecorded { get; set; }

        [JsonProperty("datelIst")]
        public string dateList { get; set; }

        [JsonIgnore]
        public string RawDate => !string.IsNullOrWhiteSpace(dateRecorded) ? dateRecorded : dateList;

        [JsonIgnore]
        public DateTime? RecordedAt
        {
            get
            {
                if (string.IsNullOrWhiteSpace(RawDate)) return null;

                // Attempt standard ISO parse first
                if (DateTime.TryParse(RawDate, out DateTime result))
                    return result;

                // Fallback for custom backend formats like "06/08/26 03:52 PM"[cite: 5]
                string[] formats = { "MM/dd/yy hh:mm tt", "MM/dd/yyyy hh:mm tt", "MM-dd-yyyy", "yyyy-MM-ddTHH:mm:ss" };
                if (DateTime.TryParseExact(RawDate, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime exactResult))
                    return exactResult;

                return null;
            }
        }

    }
    public static class HospitalResponseCodes
    {
        public const string Success = "00";

        /// <summary>Service mismatch — the pending bill changed under the cashier.</summary>
        public const string BillChanged = "06";

        public static bool IsSuccess(string code)
        {
            return string.Equals(code, Success, StringComparison.Ordinal);
        }

        /// <summary>
        /// True when the right recovery is "fetch the bill again", not "retry".
        /// The process screen uses this to decide which button to offer.
        /// </summary>
        public static bool RequiresRefetch(string code)
        {
            return code == BillChanged || code == "02";
        }

        /// <summary>True when the wallet service is simply busy and a retry is sane.</summary>
        public static bool IsTransient(string code)
        {
            return code == "09";
        }

        public static string Describe(string code, string serverMessage)
        {
            switch (code)
            {
                case "00": return serverMessage ?? "Successful.";
                case "01": return "Patient not found under this hospital. Check the patient number.";
                case "02": return "No pending bill found for this patient.";
                case "03":
                    return string.IsNullOrWhiteSpace(serverMessage)
                                  ? "This patient already has a pending bill. It must be cleared before a new one is raised."
                                  : serverMessage;
                case "04": return "That department is not available under this hospital.";
                case "05": return "One or more of the selected services are not available under this department.";
                case "06": return "The pending bill no longer matches this payment, or the wallet is short of funds. Fetch the bill again before retrying.";
                case "07": return "Revenue head not found for this service. Contact admin.";
                case "08": return "Sharing category not found for this service. Contact admin.";
                case "09": return "The wallet service is unavailable right now. Please try again in a moment.";
                case "11": return "Invalid payment method. Use Cash, Transfer or Card.";
                case "13": return "Agent account not found.";
                case "16": return "This hospital code is unknown or inactive.";
                case "97": return "No department is assigned to your account. Contact admin.";
                case "98": return "Your account was not found. Please log in again.";
                case "99":
                    return string.IsNullOrWhiteSpace(serverMessage)
                                  ? "A required field is missing."
                                  : serverMessage;
                default:
                    return string.IsNullOrWhiteSpace(serverMessage)
                        ? "Something went wrong. Please try again."
                        : serverMessage;
            }
        }
    }
    public class Transaction
    {
        [JsonProperty("date")]
        public string Date { get; set; } // POTISKUM / DAMAGUM

        [JsonProperty("datelIst")]
        public string DateList { get; set; } // DEFAULT

        [JsonIgnore]
        public string RawDate => !string.IsNullOrWhiteSpace(Date) ? Date : DateList;

        [JsonIgnore]
        public string FormattedDate
        {
            get
            {
                string raw = RawDate;
                if (string.IsNullOrWhiteSpace(raw)) return "N/A";

                // Fallbacks for array of formats[cite: 4]
                string[] DateFormats = {
                "yyyy-MM-ddTHH:mm:ss.fffffff", "yyyy-MM-ddTHH:mm:ss",
                "MM/dd/yyyy hh:mm tt", "dd/MM/yy hh:mm tt"
            };

                if (DateTime.TryParseExact(raw?.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
                    return parsed.ToString("dd MMM yyyy, hh:mm tt");
                else if (DateTime.TryParse(raw, out DateTime looseParsed))
                    return looseParsed.ToString("dd MMM yyyy, hh:mm tt");

                return raw;
            }
        }
    }

    public class StaffBillHistoryResponse
    {
        [JsonProperty("message")] public string Message { get; set; }
        [JsonProperty("code")] public string Code { get; set; }
        [JsonProperty("staffName")] public string StaffName { get; set; }
        [JsonProperty("email")] public string Email { get; set; }
        [JsonProperty("department")] public string Department { get; set; }
        [JsonProperty("hospitalName")] public string HospitalName { get; set; }
        [JsonProperty("hospitalCode")] public string HospitalCode { get; set; }
        [JsonProperty("filteredStatus")] public string FilteredStatus { get; set; }
        [JsonProperty("summary")] public StaffBillSummary Summary { get; set; }
        [JsonProperty("billGroups")] public List<StaffBillGroup> BillGroups { get; set; } = new List<StaffBillGroup>();
    }

    public class StaffBillSummary
    {
        [JsonProperty("totalBills")] public int TotalBills { get; set; }
        [JsonProperty("totalPending")] public int TotalPending { get; set; }
        [JsonProperty("totalPaid")] public int TotalPaid { get; set; }
        [JsonProperty("totalCancelled")] public int TotalCancelled { get; set; }
        [JsonProperty("grandTotal")] public decimal GrandTotal { get; set; }
    }

    public class StaffBillGroup
    {
        [JsonProperty("billGroupId")] public string BillGroupId { get; set; }
        [JsonProperty("patientNo")] public string PatientNo { get; set; }
        [JsonProperty("patientName")] public string PatientName { get; set; }
        [JsonProperty("department")] public string Department { get; set; }
        [JsonProperty("dateRaised")] public string DateRaised { get; set; }
        [JsonProperty("status")] public string Status { get; set; }
        [JsonProperty("transactionId")] public string TransactionId { get; set; }
        [JsonProperty("datePaid")] public string DatePaid { get; set; }
        [JsonProperty("paidBy")] public string PaidBy { get; set; }
        [JsonProperty("grandTotal")] public decimal GrandTotal { get; set; }
        [JsonProperty("totalServices")] public int TotalServices { get; set; }
        [JsonProperty("services")] public List<StaffBillService> Services { get; set; } = new List<StaffBillService>();

        // ── UI Binding Helpers ──
        [JsonIgnore]
        public string FormattedDate => DateTime.TryParse(DateRaised, out DateTime d) ? d.ToString("MMM dd, yyyy - hh:mm tt") : DateRaised;

        [JsonIgnore]
        public Color StatusColor => string.Equals(Status, "Paid", StringComparison.OrdinalIgnoreCase) ? Color.FromHex("#0F6E56") : Color.FromHex("#B7791F");

        [JsonIgnore]
        public Color StatusBgColor => string.Equals(Status, "Paid", StringComparison.OrdinalIgnoreCase) ? Color.FromHex("#E1F5EE") : Color.FromHex("#FEFCBF");

        [JsonIgnore]
        public string DisplayId => !string.IsNullOrWhiteSpace(BillGroupId) ? BillGroupId : (TransactionId ?? "N/A");
    }

    public class StaffBillService
    {
        [JsonProperty("id")] public int Id { get; set; }
        [JsonProperty("serviceName")] public string ServiceName { get; set; }
        [JsonProperty("amount")] public decimal Amount { get; set; }
        [JsonProperty("notes")] public string Notes { get; set; }
    }
}
