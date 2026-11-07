using System;

namespace SRMS.Desktop.Models
{
    public sealed class Subscription
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }
        public string PlanCode { get; set; }
        public string Status { get; set; }
        public DateTime StartedUtc { get; set; }
        public DateTime? ExpiresUtc { get; set; }
        public DateTime? GraceUntilUtc { get; set; }
        public string Source { get; set; }
        public string LegacyLicenseKey { get; set; }
        public DateTime UpdatedUtc { get; set; }

        public bool IsUsable(DateTime utcNow)
        {
            if (!string.Equals(Status, "Active", StringComparison.OrdinalIgnoreCase))
                return false;
            if (!ExpiresUtc.HasValue || ExpiresUtc.Value > utcNow)
                return true;
            return GraceUntilUtc.HasValue && GraceUntilUtc.Value > utcNow;
        }
    }
}
