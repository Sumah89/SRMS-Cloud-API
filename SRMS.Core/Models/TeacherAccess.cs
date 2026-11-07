using System;

namespace SRMS.Desktop.Models
{
    public class TeacherAccess
    {
        public int Id { get; set; }
        public int TeacherId { get; set; }
        public int? ClassId { get; set; } // Null = all classes
        public int? SubjectId { get; set; } // Null = all subjects
        public DateTime AssignedDate { get; set; }
        public bool IsActive { get; set; }
        public string AccessLevel { get; set; } // "Full", "Limited", "ReadOnly"

        // Navigation properties
        public string TeacherName { get; set; }
        public string ClassName { get; set; }
        public string SubjectName { get; set; }
    }
}