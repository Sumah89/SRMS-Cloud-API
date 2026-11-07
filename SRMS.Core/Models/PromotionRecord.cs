using System;

namespace SRMS.Desktop.Models
{
    /// <summary>
    /// Single source of truth for a student's promotion outcome for one academic year.
    /// Written once when the promotion is computed and frozen; read by every report,
    /// transcript, and audit surface thereafter.
    /// </summary>
    public class PromotionRecord
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public int SchoolId { get; set; }
        public int ClassId { get; set; }
        public string AcademicYear { get; set; }

        // Frozen computed values at time of promotion
        public decimal AnnualAverage { get; set; }
        public int CorePassed { get; set; }
        public int OptionalPassed { get; set; }
        public int TotalPassed { get; set; }
        public int TotalCore { get; set; }
        public int TotalOptional { get; set; }
        public int TotalSubjects { get; set; }
        public int Rank { get; set; }

        // Outcome
        public bool IsPromoted { get; set; }
        public string NextClass { get; set; }
        public string Reason { get; set; }

        // Snapshot of the criteria used, so future audits can reconstruct why
        // the decision was made even after criteria have been edited
        public string CriteriaSnapshotJson { get; set; }

        // Audit
        public DateTime ComputedAt { get; set; }
        public int? ComputedByUserId { get; set; }
        public string ComputedByName { get; set; }

        // Optional: was this record later overridden by an admin?
        public bool IsManuallyOverridden { get; set; }
        public string OverrideReason { get; set; }
        public DateTime? OverriddenAt { get; set; }
        public int? OverriddenByUserId { get; set; }
    }
}