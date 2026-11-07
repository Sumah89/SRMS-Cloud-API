using System;

namespace SRMS.Desktop.Models
{
    public class Message
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public string MessageType { get; set; } // "Announcement", "FeeReminder", "ExamAlert", "General"
        public int? TargetClassId { get; set; } // Null = all classes
        public int? TargetStudentId { get; set; } // Null = all students
        public string TargetRole { get; set; } // "Teachers", "Parents", "All"
        public string SentBy { get; set; }
        public DateTime SentDate { get; set; }
        public bool IsRead { get; set; }
        public bool IsActive { get; set; }

        // Navigation
        public string ClassName { get; set; }
        public string StudentName { get; set; }
    }
}