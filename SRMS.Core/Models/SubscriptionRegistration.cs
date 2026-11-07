using System;

namespace SRMS.Desktop.Models
{
    public sealed class SubscriptionRegistrationRequest
    {
        public string SchoolName { get; set; }
        public string District { get; set; }
        public string Location { get; set; }
        public string PrincipalName { get; set; }
        public string Contact { get; set; }
        public string AdminFullName { get; set; }
        public string AdminUsername { get; set; }
        public string AdminPassword { get; set; }
        public string PlanCode { get; set; }
        public string Provider { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; }
    }

    public sealed class SubscriptionRegistrationResult
    {
        public int SchoolId { get; set; }
        public int UserId { get; set; }
        public string PaymentReference { get; set; }
        public string Status { get; set; }
        public bool Activated { get; set; }
        public DateTime CreatedUtc { get; set; }
    }
}
