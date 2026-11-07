// Models/AttendanceRecord.cs
using System;

namespace SRMS.Desktop.Models
{
    public class AttendanceRecord
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public DateTime Date { get; set; }
        public string Status { get; set; } // Present, Absent, Late, Excused
        public string AcademicYear { get; set; }
        public int? Term { get; set; }
        public string Remarks { get; set; }
    }
}