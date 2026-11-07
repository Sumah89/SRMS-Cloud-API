using System;

namespace SRMS.Desktop.Models
{
    /// <summary>
    /// Maps subjects to specific rows in the Excel report card template
    /// </summary>
    public class ReportSubjectRowMapping
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }
        public int? ClassId { get; set; }
        public int SubjectId { get; set; }

        /// <summary>Excel row number (8-18 for subjects)</summary>
        public int ExcelRowNumber { get; set; }

        /// <summary>Display order on report card</summary>
        public int DisplayOrder { get; set; }

        public bool IsVisible { get; set; } = true;
        public DateTime CreatedDate { get; set; }

        // Navigation properties
        public string SubjectName { get; set; }
        public string ClassName { get; set; }
    }
}