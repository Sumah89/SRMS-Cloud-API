using System;

namespace SRMS.Desktop.Models
{
    public class ReportSettings
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }
        public string ReportName { get; set; }
        public string ReportType { get; set; } // Performance, Attendance, Fee, Combined
        public bool IncludeCharts { get; set; } = true;
        public bool IncludeRankings { get; set; } = true;
        public bool IncludeSubjectBreakdown { get; set; } = true;
        public bool IncludeAttendance { get; set; } = true;
        public bool IncludeFeeSummary { get; set; } = true;
        public string PaperSize { get; set; } = "A4";
        public string Orientation { get; set; } = "Portrait";
        public string PrimaryColor { get; set; } = "41,128,185";
        public string SecondaryColor { get; set; } = "52,152,219";
        public bool ShowSchoolLogo { get; set; } = true;
        public bool ShowSignatureLines { get; set; } = true;
        public string PrincipalName { get; set; }
        public string HeadTeacherName { get; set; }
        public DateTime LastModified { get; set; }
        public string ModifiedBy { get; set; }

        public string GetFormattedPrimaryColor()
        {
            var parts = PrimaryColor.Split(',');
            if (parts.Length == 3)
                return $"rgb({parts[0]}, {parts[1]}, {parts[2]})";
            return "#2c3e50";
        }

        public string GetFormattedSecondaryColor()
        {
            var parts = SecondaryColor.Split(',');
            if (parts.Length == 3)
                return $"rgb({parts[0]}, {parts[1]}, {parts[2]})";
            return "#3498db";
        }
    }

    public class ReportTemplate
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }
        public string TemplateName { get; set; }
        public string TemplateType { get; set; } // ReportCard, Transcript, TransferCert
        public string TemplateContent { get; set; } // HTML/CSS content
        public bool IsDefault { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
        public string CreatedBy { get; set; }
    }

    public class ExportHistory
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }
        public int? UserId { get; set; }
        public string ExportType { get; set; } // PDF, Excel, CSV, HTML
        public string ReportType { get; set; } // Performance, Fee, Attendance
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public int RecordCount { get; set; }
        public DateTime ExportDate { get; set; }
        public string ExportedBy { get; set; }
    }
}