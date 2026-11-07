using System;

namespace SRMS.Desktop.Models
{
    public class School
    {
        public int Id { get; set; }
        public string SchoolName { get; set; }
        public string District { get; set; }
        public string Town { get; set; }
        public string Chiefdom { get; set; }
        public string Address { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public string PrincipalName { get; set; }
        public string LogoPath { get; set; }
        public string SubscriptionKey { get; set; }
        public DateTime SubscriptionExpiryDate { get; set; }
        public string LicenseKey { get => SubscriptionKey; set => SubscriptionKey = value; }
        public DateTime LicenseExpiryDate { get => SubscriptionExpiryDate; set => SubscriptionExpiryDate = value; }
        public bool IsActive { get; set; }

        /// <summary>
        /// Result type: "FiveTests" or "SixTests"
        /// SSS1 is NOT a school result type - it's determined by class name
        /// </summary>
        public string ResultType { get; set; } = "FiveTests";

        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }

        /// <summary>
        /// True if the school has an active (or unlimited) subscription.
        /// DateTime.MaxValue on SubscriptionExpiryDate is treated as unlimited.
        /// </summary>
        public bool IsSubscriptionActive
        {
            get
            {
                if (SubscriptionExpiryDate == DateTime.MaxValue) return true;
                if (SubscriptionExpiryDate == default(DateTime)) return false;
                return SubscriptionExpiryDate >= DateTime.Now;
            }
        }

        /// <summary>
        /// Days remaining until subscription expiry. Returns int.MaxValue for unlimited,
        /// 0 for expired or unset.
        /// </summary>
        public int DaysUntilExpiry
        {
            get
            {
                if (SubscriptionExpiryDate == DateTime.MaxValue) return int.MaxValue;
                if (SubscriptionExpiryDate == default(DateTime)) return 0;
                var days = (SubscriptionExpiryDate.Date - DateTime.Now.Date).Days;
                return days < 0 ? 0 : days;
            }
        }
    }
}