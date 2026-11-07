using System;

namespace SRMS.Desktop.Models
{
    /// <summary>
    /// Represents a parent's paid subscription to SRMS. One row per parent user.
    /// Created as Pending when the parent initiates payment via the mobile app,
    /// activated by the payment webhook. Parents without an Active row cannot
    /// sign in (Cloud returns 402 Payment Required).
    /// </summary>
    public sealed class ParentSubscription
    {
        public int Id { get; set; }
        public string SyncId { get; set; }
        public int SchoolId { get; set; }
        public int ParentUserId { get; set; }
        public int? StudentId { get; set; }
        public string PlanCode { get; set; } = "monthly";
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "SLL";
        public string Provider { get; set; } = "OrangeMoney";
        public string Status { get; set; } = "Pending";
        public string PaymentReference { get; set; }
        public string ProviderTransactionId { get; set; }
        public DateTime? StartedUtc { get; set; }
        public DateTime? ExpiresUtc { get; set; }
        public DateTime? GraceUntilUtc { get; set; }
        public string Source { get; set; } = "mobile";
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }

        public bool IsUsable(DateTime utcNow)
        {
            if (!string.Equals(Status, "Active", StringComparison.OrdinalIgnoreCase)) return false;
            return ExpiresUtc.HasValue && ExpiresUtc.Value > utcNow;
        }
    }
}