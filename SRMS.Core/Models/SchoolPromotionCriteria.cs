using System;
using System.Collections.Generic;
using System.Linq;

namespace SRMS.Desktop.Models
{
    public class SchoolPromotionCriteria
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }
        public int? ClassId { get; set; }
        public string CriteriaType { get; set; } = "Average";
        public decimal? MinAveragePercentage { get; set; } = 50;
        public int? MinCoreSubjectsPassed { get; set; } = 3;
        public int? MinOptionalSubjectsPassed { get; set; } = 3;
        public int? MinTotalSubjectsPassed { get; set; } = 6;
        public decimal CoreSubjectPassMark { get; set; } = 50;
        public decimal OptionalSubjectPassMark { get; set; } = 50;
        public decimal TotalPassMark { get; set; } = 50;

        // ── NEW: English / Maths requirement ──
        public bool RequireEnglishOrMaths { get; set; } = false;
        public decimal EnglishMathsPassMark { get; set; } = 50;

        public bool IncludeAttendance { get; set; } = false;
        public decimal MinAttendancePercentage { get; set; } = 75;
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }

        // Additional properties for display
        public string ClassName { get; set; }

        public bool IsSchoolWide => !ClassId.HasValue;

        // Helper property for group name (returns ClassName for class-specific criteria)
        public string GroupName
        {
            get { return IsSchoolWide ? null : ClassName; }
            set { if (!IsSchoolWide) ClassName = value; }
        }

        public string GetDescription()
        {
            var parts = new List<string>();
            string[] types = (CriteriaType ?? "").Split(',');

            foreach (string type in types)
            {
                switch (type.Trim())
                {
                    case "Average":
                        parts.Add($"Annual Average ≥ {MinAveragePercentage ?? 0}%");
                        break;
                    case "CoreSubjects":
                        parts.Add($"Core: Pass ≥ {MinCoreSubjectsPassed ?? 0} (≥{CoreSubjectPassMark}%)");
                        break;
                    case "EnglishMaths":
                        parts.Add($"English/Maths pass ≥ {EnglishMathsPassMark}%");
                        break;
                    case "OptionalSubjects":
                        parts.Add($"Optional: Pass ≥ {MinOptionalSubjectsPassed ?? 0} (≥{OptionalSubjectPassMark}%)");
                        break;
                    case "TotalSubjects":
                        parts.Add($"Total: Pass ≥ {MinTotalSubjectsPassed ?? 0} (≥{TotalPassMark}%)");
                        break;
                }
            }

            if (IncludeAttendance)
                parts.Add($"Attendance ≥ {MinAttendancePercentage}%");

            return parts.Count > 0 ? string.Join(" AND ", parts) : "NOT CONFIGURED";
        }

        public string GetShortDescription()
        {
            var parts = new List<string>();
            string[] types = (CriteriaType ?? "").Split(',');

            foreach (string type in types)
            {
                switch (type.Trim())
                {
                    case "Average": parts.Add($"Avg≥{MinAveragePercentage ?? 0}%"); break;
                    case "CoreSubjects": parts.Add($"Core≥{MinCoreSubjectsPassed ?? 0}"); break;
                    case "EnglishMaths": parts.Add($"Eng/Math≥{EnglishMathsPassMark}%"); break;
                    case "OptionalSubjects": parts.Add($"Opt≥{MinOptionalSubjectsPassed ?? 0}"); break;
                    case "TotalSubjects": parts.Add($"Total≥{MinTotalSubjectsPassed ?? 0}"); break;
                }
            }

            if (IncludeAttendance)
                parts.Add($"Attend≥{MinAttendancePercentage}%");

            return parts.Count > 0 ? string.Join(", ", parts) : "Not configured";
        }

        public SchoolPromotionCriteria Clone()
        {
            return new SchoolPromotionCriteria
            {
                Id = this.Id,
                SchoolId = this.SchoolId,
                ClassId = this.ClassId,
                CriteriaType = this.CriteriaType,
                MinAveragePercentage = this.MinAveragePercentage,
                MinCoreSubjectsPassed = this.MinCoreSubjectsPassed,
                MinOptionalSubjectsPassed = this.MinOptionalSubjectsPassed,
                MinTotalSubjectsPassed = this.MinTotalSubjectsPassed,
                CoreSubjectPassMark = this.CoreSubjectPassMark,
                OptionalSubjectPassMark = this.OptionalSubjectPassMark,
                TotalPassMark = this.TotalPassMark,
                RequireEnglishOrMaths = this.RequireEnglishOrMaths,
                EnglishMathsPassMark = this.EnglishMathsPassMark,
                IncludeAttendance = this.IncludeAttendance,
                MinAttendancePercentage = this.MinAttendancePercentage,
                ClassName = this.ClassName,
                CreatedDate = this.CreatedDate,
                ModifiedDate = this.ModifiedDate
            };
        }
    }
}