using System;

namespace SRMS.Desktop.Models
{
    public class ParentCode
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }
        public int StudentId { get; set; }
        public string AccessCode { get; set; }
        public string ParentName { get; set; }
        public string ParentPhone { get; set; }
        public string ParentEmail { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public int UsageCount { get; set; }
        public int MaxUsage { get; set; } = 5;
        public bool IsActive { get; set; }
        public DateTime SubscriptionExpiryDate { get; set; }

        // Navigation
        public string StudentName { get; set; }
        public string AdmissionNumber { get; set; }
        public string ClassName { get; set; }
    }
}