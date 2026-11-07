using System;

namespace SRMS.Desktop.Models
{
    /// <summary>
    /// Global parent identity — one row per phone number, shared across all schools.
    /// A parent may have children in multiple schools; <see cref="Students.ParentPhone"/>
    /// is the join key.
    /// </summary>
    public class Parent
    {
        public int Id { get; set; }

        /// <summary>E.164 phone number, e.g. "+23276123456". Unique.</summary>
        public string PhoneE164 { get; set; }

        public string FullName { get; set; }

        /// <summary>Device the parent is bound to (single-device login). Null = unbound.</summary>
        public string DeviceId { get; set; }

        /// <summary>SHA-256 hash of the 4-digit PIN. Null until PIN is set.</summary>
        public string PinHash { get; set; }

        /// <summary>True while a ParentSubscriptions row is Active.</summary>
        public bool IsSubscribed { get; set; }

        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }
}