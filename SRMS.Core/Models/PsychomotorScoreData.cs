using System;

namespace SRMS.Desktop.Models
{
    public class PsychomotorScoreData
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public int Term { get; set; }
        public string AcademicYear { get; set; }

        // Term 1 - Two test scores per skill
        public int Term1IndividualSportsT1 { get; set; } = 3;
        public int Term1IndividualSportsT2 { get; set; } = 3;
        public int Term1TeamPlayerT1 { get; set; } = 3;
        public int Term1TeamPlayerT2 { get; set; } = 3;
        public int Term1AthleticsT1 { get; set; } = 3;
        public int Term1AthleticsT2 { get; set; } = 3;
        public int Term1DrawingT1 { get; set; } = 3;
        public int Term1DrawingT2 { get; set; } = 3;
        public int Term1HandwritingT1 { get; set; } = 3;
        public int Term1HandwritingT2 { get; set; } = 3;

        // Term 2
        public int Term2IndividualSportsT1 { get; set; } = 3;
        public int Term2IndividualSportsT2 { get; set; } = 3;
        public int Term2TeamPlayerT1 { get; set; } = 3;
        public int Term2TeamPlayerT2 { get; set; } = 3;
        public int Term2AthleticsT1 { get; set; } = 3;
        public int Term2AthleticsT2 { get; set; } = 3;
        public int Term2DrawingT1 { get; set; } = 3;
        public int Term2DrawingT2 { get; set; } = 3;
        public int Term2HandwritingT1 { get; set; } = 3;
        public int Term2HandwritingT2 { get; set; } = 3;

        // Term 3
        public int Term3IndividualSportsT1 { get; set; } = 3;
        public int Term3IndividualSportsT2 { get; set; } = 3;
        public int Term3TeamPlayerT1 { get; set; } = 3;
        public int Term3TeamPlayerT2 { get; set; } = 3;
        public int Term3AthleticsT1 { get; set; } = 3;
        public int Term3AthleticsT2 { get; set; } = 3;
        public int Term3DrawingT1 { get; set; } = 3;
        public int Term3DrawingT2 { get; set; } = 3;
        public int Term3HandwritingT1 { get; set; } = 3;
        public int Term3HandwritingT2 { get; set; } = 3;

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
            int total = Term1IndividualSportsT1 + Term1IndividualSportsT2 +
                       Term1TeamPlayerT1 + Term1TeamPlayerT2 +
                       Term1AthleticsT1 + Term1AthleticsT2 +
                       Term1DrawingT1 + Term1DrawingT2 +
                       Term1HandwritingT1 + Term1HandwritingT2;
            return total / 10m;
        }

        public decimal GetTerm2Average()
        {
            int total = Term2IndividualSportsT1 + Term2IndividualSportsT2 +
                       Term2TeamPlayerT1 + Term2TeamPlayerT2 +
                       Term2AthleticsT1 + Term2AthleticsT2 +
                       Term2DrawingT1 + Term2DrawingT2 +
                       Term2HandwritingT1 + Term2HandwritingT2;
            return total / 10m;
        }

        public decimal GetTerm3Average()
        {
            int total = Term3IndividualSportsT1 + Term3IndividualSportsT2 +
                       Term3TeamPlayerT1 + Term3TeamPlayerT2 +
                       Term3AthleticsT1 + Term3AthleticsT2 +
                       Term3DrawingT1 + Term3DrawingT2 +
                       Term3HandwritingT1 + Term3HandwritingT2;
            return total / 10m;
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