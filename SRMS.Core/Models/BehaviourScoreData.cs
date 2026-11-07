using System;

namespace SRMS.Desktop.Models
{
    public class BehaviourScoreData
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public int Term { get; set; }
        public string AcademicYear { get; set; }

        // Simplified properties for database operations (used by DatabaseHelper)
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

        // Term 1 - Two test scores per trait (for detailed form display)
        public int Term1ClassParticipationT1 { get; set; } = 3;
        public int Term1ClassParticipationT2 { get; set; } = 3;
        public int Term1HonestyT1 { get; set; } = 3;
        public int Term1HonestyT2 { get; set; } = 3;
        public int Term1EffortT1 { get; set; } = 3;
        public int Term1EffortT2 { get; set; } = 3;
        public int Term1SelfDisciplineT1 { get; set; } = 3;
        public int Term1SelfDisciplineT2 { get; set; } = 3;
        public int Term1PunctualityT1 { get; set; } = 3;
        public int Term1PunctualityT2 { get; set; } = 3;
        public int Term1SchoolInvolvementT1 { get; set; } = 3;
        public int Term1SchoolInvolvementT2 { get; set; } = 3;
        public int Term1NeatnessT1 { get; set; } = 3;
        public int Term1NeatnessT2 { get; set; } = 3;

        // Term 2 - Two test scores per trait
        public int Term2ClassParticipationT1 { get; set; } = 3;
        public int Term2ClassParticipationT2 { get; set; } = 3;
        public int Term2HonestyT1 { get; set; } = 3;
        public int Term2HonestyT2 { get; set; } = 3;
        public int Term2EffortT1 { get; set; } = 3;
        public int Term2EffortT2 { get; set; } = 3;
        public int Term2SelfDisciplineT1 { get; set; } = 3;
        public int Term2SelfDisciplineT2 { get; set; } = 3;
        public int Term2PunctualityT1 { get; set; } = 3;
        public int Term2PunctualityT2 { get; set; } = 3;
        public int Term2SchoolInvolvementT1 { get; set; } = 3;
        public int Term2SchoolInvolvementT2 { get; set; } = 3;
        public int Term2NeatnessT1 { get; set; } = 3;
        public int Term2NeatnessT2 { get; set; } = 3;

        // Term 3 - Two test scores per trait
        public int Term3ClassParticipationT1 { get; set; } = 3;
        public int Term3ClassParticipationT2 { get; set; } = 3;
        public int Term3HonestyT1 { get; set; } = 3;
        public int Term3HonestyT2 { get; set; } = 3;
        public int Term3EffortT1 { get; set; } = 3;
        public int Term3EffortT2 { get; set; } = 3;
        public int Term3SelfDisciplineT1 { get; set; } = 3;
        public int Term3SelfDisciplineT2 { get; set; } = 3;
        public int Term3PunctualityT1 { get; set; } = 3;
        public int Term3PunctualityT2 { get; set; } = 3;
        public int Term3SchoolInvolvementT1 { get; set; } = 3;
        public int Term3SchoolInvolvementT2 { get; set; } = 3;
        public int Term3NeatnessT1 { get; set; } = 3;
        public int Term3NeatnessT2 { get; set; } = 3;

        // Calculated averages
        public decimal Term1Average { get; set; }
        public decimal Term2Average { get; set; }
        public decimal Term3Average { get; set; }
        public decimal FinalAverage { get; set; }
        public string FinalGrade { get; set; }

        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }

        public decimal GetTerm1Average()
        {
            int total = Term1ClassParticipationT1 + Term1ClassParticipationT2 +
                       Term1HonestyT1 + Term1HonestyT2 +
                       Term1EffortT1 + Term1EffortT2 +
                       Term1SelfDisciplineT1 + Term1SelfDisciplineT2 +
                       Term1PunctualityT1 + Term1PunctualityT2 +
                       Term1SchoolInvolvementT1 + Term1SchoolInvolvementT2 +
                       Term1NeatnessT1 + Term1NeatnessT2;
            return total / 14m;
        }

        public decimal GetTerm2Average()
        {
            int total = Term2ClassParticipationT1 + Term2ClassParticipationT2 +
                       Term2HonestyT1 + Term2HonestyT2 +
                       Term2EffortT1 + Term2EffortT2 +
                       Term2SelfDisciplineT1 + Term2SelfDisciplineT2 +
                       Term2PunctualityT1 + Term2PunctualityT2 +
                       Term2SchoolInvolvementT1 + Term2SchoolInvolvementT2 +
                       Term2NeatnessT1 + Term2NeatnessT2;
            return total / 14m;
        }

        public decimal GetTerm3Average()
        {
            int total = Term3ClassParticipationT1 + Term3ClassParticipationT2 +
                       Term3HonestyT1 + Term3HonestyT2 +
                       Term3EffortT1 + Term3EffortT2 +
                       Term3SelfDisciplineT1 + Term3SelfDisciplineT2 +
                       Term3PunctualityT1 + Term3PunctualityT2 +
                       Term3SchoolInvolvementT1 + Term3SchoolInvolvementT2 +
                       Term3NeatnessT1 + Term3NeatnessT2;
            return total / 14m;
        }

        public void UpdateAverages()
        {
            Term1Average = GetTerm1Average();
            Term2Average = GetTerm2Average();
            Term3Average = GetTerm3Average();
            FinalAverage = (Term1Average + Term2Average + Term3Average) / 3;
            FinalGrade = GetGrade(FinalAverage);
        }

        private string GetGrade(decimal average)
        {
            if (average >= 4.5m) return "Excellent";
            if (average >= 3.5m) return "Very Good";
            if (average >= 2.5m) return "Good";
            if (average >= 1.5m) return "Fair";
            return "Poor";
        }
    }
}