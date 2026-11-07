using System;

namespace SRMS.Desktop.Models
{
    public class ExamSession
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }
        public string SessionName { get; set; }
        public int Term { get; set; }
        public string AcademicYear { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsActive { get; set; }
        public bool AllowEditing { get; set; } = true;
        public string Status { get; set; } // "Upcoming", "Active", "Closed", "Archived"
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
    }
}