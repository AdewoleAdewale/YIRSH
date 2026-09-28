using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace YIRSHospital.Services
{
    public class VerifyTransactionResponse
    {
        public string message { get; set; }
        public string code { get; set; }
        public bool isRecent { get; set; }
        public string paymentStatus { get; set; }
        public string transactionId { get; set; }
        public string date { get; set; }
        public string revenueHead { get; set; }
        public string department { get; set; }
        public string paymentMethod { get; set; }
        public string paymentReference { get; set; }
        public string status { get; set; }
        public string patientNo { get; set; }
        public string patientName { get; set; }
        public string gender { get; set; }
        public string phoneNumber { get; set; }
        public decimal totalAmount { get; set; }
        public int totalServices { get; set; }
        public string processedBy { get; set; }
        public string agentEmail { get; set; }
        public List<VerifyTransactionService> services { get; set; }
    }

    public class VerifyTransactionService
    {
        public string serviceTypeName { get; set; }
        public string department { get; set; }
        public decimal amount { get; set; }
    }
}
