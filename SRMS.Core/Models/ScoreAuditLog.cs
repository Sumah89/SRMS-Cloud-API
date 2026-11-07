using System;

namespace SRMS.Desktop.Models
{
    /// <summary>
    /// Immutable record of a single score change. Once written, an audit row is
    /// never updated or deleted by the application.
    /// </summary>
    public class ScoreAuditLog
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public int SubjectId { get; set; }
        public int Term { get; set; }
        public string AcademicYear { get; set; }

        /// <summary>Which field changed: "T1", "T2", "T3", "T4", "T5", "T6", "Exam", "FinalScore", "Grade".</summary>
        public string FieldName { get; set; }

        /// <summary>Previous value before this change, as a string (nullable for new rows).</summary>
        public string OldValue { get; set; }

        /// <summary>New value after this change, as a string.</summary>
        public string NewValue { get; set; }

        /// <summary>What kind of operation triggered the change: Insert, Update, Delete.</summary>
        public string OperationType { get; set; }

        /// <summary>Where the change originated: "ScoreEntryPopupForm", "FullResultEntryForm", "DatabaseHelper", "BulkImport", etc.</summary>
        public string Source { get; set; }

        // Who
        public int? UserId { get; set; }
        public string UserName { get; set; }
        public string UserRole { get; set; }

        // When (server/UTC; do NOT use local time for audit)
        public DateTime ChangedAt { get; set; }

        // Optional context
        public string Remarks { get; set; }
    }
}