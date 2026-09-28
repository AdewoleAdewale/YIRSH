using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace YIRSHospital.Services
{
    public class VerifiedService
    {
        public string ServiceTypeName { get; set; }
        public string Department { get; set; }
        public decimal Amount { get; set; }
        public decimal OsoftPercent { get; set; }
        public decimal RemitaPercent { get; set; }

        public string AmountDisplay
        {
            get { return "\u20A6" + Amount.ToString("N2", CultureInfo.InvariantCulture); }
        }
    }

    public class VerifyTransactionResponse
    {
        public string Message { get; set; }
        public string Code { get; set; }
        public bool IsRecent { get; set; }
        public string PaymentStatus { get; set; }
        public string TransactionId { get; set; }
        public DateTime? Date { get; set; }
        public string RevenueHead { get; set; }
        public string Department { get; set; }
        public string PaymentMethod { get; set; }
        public string PaymentReference { get; set; }
        public string DebitRef { get; set; }
        public string Status { get; set; }
        public string PatientNo { get; set; }
        public string PatientName { get; set; }
        public string Gender { get; set; }
        public string PhoneNumber { get; set; }
        public decimal TotalAmount { get; set; }
        public int TotalServices { get; set; }
        public List<VerifiedService> Services { get; set; }
        public string ProcessedBy { get; set; }
        public string AgentEmail { get; set; }

        // API returns code "00" on success
        public bool IsSuccess
        {
            get { return Code == "00"; }
        }

        public string TotalAmountDisplay
        {
            get { return "\u20A6" + TotalAmount.ToString("N2", CultureInfo.InvariantCulture); }
        }

        public string DateDisplay
        {
            get { return Date.HasValue ? Date.Value.ToString("dd MMM yyyy, hh:mm tt", CultureInfo.InvariantCulture) : "-"; }
        }

        public string ProcessedByDisplay
        {
            get { return (ProcessedBy ?? string.Empty).Trim(); }
        }
    }
}
