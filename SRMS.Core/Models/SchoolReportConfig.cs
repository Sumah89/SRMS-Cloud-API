using System;

namespace SRMS.Desktop.Models
{
    /// <summary>
    /// School-specific report card configuration
    /// </summary>
    public class SchoolReportConfig
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }

        /// <summary>Result Type System: "FiveTests" or "SixTests"</summary>
        public string ResultType { get; set; } = "FiveTests";

        /// <summary>Number of terms (2 or 3)</summary>
        public int NumberOfTerms { get; set; } = 3;

        /// <summary>Paper orientation: 'Portrait' or 'Landscape'</summary>
        public string PaperOrientation { get; set; } = "Portrait";

        /// <summary>Show Behaviour/Affective section on report card</summary>
        public bool ShowBehaviourSection { get; set; } = true;

        /// <summary>Show Psychomotor/Skills section on report card</summary>
        public bool ShowPsychomotorSection { get; set; } = true;

        /// <summary>Show Attendance section on report card</summary>
        public bool ShowAttendanceSection { get; set; } = true;

        /// <summary>Show Subject Analysis (core/optional passed)</summary>
        public bool ShowSubjectAnalysis { get; set; } = true;

        /// <summary>Path to school logo for header</summary>
        public string HeaderLogoPath { get; set; }

        /// <summary>Custom footer text</summary>
        public string FooterText { get; set; }

        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }

        // Helper properties
        public bool IsLandscape => PaperOrientation?.Equals("Landscape", StringComparison.OrdinalIgnoreCase) == true;
    }
}