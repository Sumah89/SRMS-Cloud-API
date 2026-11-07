using System;

namespace SRMS.Desktop.Models
{
    public class Subject
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }
        public string SubjectCode { get; set; }
        public string SubjectName { get; set; }
        public bool IsCore { get; set; }
        public int? ClassId { get; set; }
        public decimal PassMark { get; set; }
        public string AcademicYear { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
    }
}