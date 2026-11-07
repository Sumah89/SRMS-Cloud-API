using System;

namespace SRMS.Desktop.Models
{
    public class StudentEnrollment
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public int ClassId { get; set; }
        public string AcademicYear { get; set; }
        public int? Term { get; set; }
        public DateTime EnrollmentDate { get; set; }
        public DateTime? ExitDate { get; set; }
        public string Status { get; set; }
        public string ExitReason { get; set; }
        public bool IsCurrent { get; set; }
        public int? BatchId { get; set; }          // ← links to PromotionBatches
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }

        // Joined
        public string ClassName { get; set; }
    }
}