using System;

namespace SRMS.Desktop.Models
{
    /// <summary>
    /// Represents one click of "Promote Selected" or "Promote All in Class".
    /// Every batch is reversible as a unit.
    /// </summary>
    public class PromotionBatch
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }
        public int SourceClassId { get; set; }
        public int DestinationClassId { get; set; }
        public string SourceAcademicYear { get; set; }
        public string DestinationAcademicYear { get; set; }
        public int StudentCount { get; set; }
        public int? PerformedByUserId { get; set; }
        public string PerformedByName { get; set; }
        public string PerformedByRole { get; set; }
        public DateTime BatchTimestamp { get; set; }
        public DateTime? RevertedAt { get; set; }
        public int? RevertedByUserId { get; set; }
        public string RevertedByName { get; set; }

        // Joined, not stored
        public string SourceClassName { get; set; }
        public string DestinationClassName { get; set; }
    }
}