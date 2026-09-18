using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

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

    // ── 4. Get Patient Bill Models ──
    public class PatientBillResponse
    {
        [JsonProperty("message")] public string Message { get; set; }
        [JsonProperty("code")] public string Code { get; set; }
        [JsonProperty("patientName")] public string PatientName { get; set; }
        [JsonProperty("patientNo")] public string PatientNo { get; set; }
        [JsonProperty("gender")] public string Gender { get; set; }
        [JsonProperty("phoneNumber")] public string PhoneNumber { get; set; }
        [JsonProperty("hospitalName")] public string HospitalName { get; set; }
        [JsonProperty("hospitalCode")] public string HospitalCode { get; set; }
        [JsonProperty("billGroupId")] public string BillGroupId { get; set; }
        [JsonProperty("department")] public string Department { get; set; }
        [JsonProperty("grandTotal")] public decimal GrandTotal { get; set; }
        [JsonProperty("totalServices")] public int TotalServices { get; set; }
        [JsonProperty("services")] public List<PendingBillService> Services { get; set; } = new List<PendingBillService>();
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

    // ── 5. Process Patient Bill Models ──
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

    // ── 6. Confirm Patient Payment Models ──
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
}
