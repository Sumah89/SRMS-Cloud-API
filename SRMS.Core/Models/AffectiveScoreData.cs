using System;

namespace SRMS.Desktop.Models
{
    /// <summary>
    /// Represents affective (behaviour/conduct) scores for a student
    /// This is an alias/simplified version of BehaviourScoreData
    /// </summary>
    public class AffectiveScoreData
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public int Term { get; set; }
        public string AcademicYear { get; set; }

        // Behaviour traits (rated 1-5) - Simplified version matching DatabaseHelper expectations
        public int Punctuality { get; set; } = 3;
        public int Attendance { get; set; } = 3;
        public int Neatness { get; set; } = 3;
        public int Honesty { get; set; } = 3;
        public int Cooperation { get; set; } = 3;
        public int Leadership { get; set; } = 3;
        public int Attentiveness { get; set; } = 3;
        public int Respect { get; set; } = 3;
        public int Diligence { get; set; } = 3;
        public int Conduct { get; set; } = 3;
        public int Interest { get; set; } = 3;
        public int EmotionalStability { get; set; } = 3;

        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
    }
}