using System;
using System.Collections.Generic;
using System.Linq;

namespace SRMS.Desktop.Models
{
    /// <summary>
    /// Represents subject scores for a student
    /// Improved with consistent calculation logic and validation
    /// </summary>
    public class SubjectScoreData
    {
        // Identification
        public int SubjectId { get; set; }
        public string SubjectName { get; set; }
        public bool IsCore { get; set; }
        public int DisplayOrder { get; set; }

        // Test scores (nullable to distinguish between 0 and not entered)
        public decimal? T1 { get; set; }
        public decimal? T2 { get; set; }
        public decimal? T3 { get; set; }
        public decimal? T4 { get; set; }
        public decimal? T5 { get; set; }
        public decimal? T6 { get; set; }
        public decimal? Exam { get; set; }

        // Calculated values (stored or computed)
        public decimal Term1Mean { get; set; }
        public decimal Term2Mean { get; set; }
        public decimal Term3Mean { get; set; }
        public decimal AnnualMean { get; set; }
        public decimal Percent60 { get; set; }
        public decimal Percent40 { get; set; }
        public decimal FinalScore { get; set; }
        public string Grade { get; set; }
        public bool IsPassed { get; set; }

        // Metadata
        public int StudentId { get; set; }
        public int Term { get; set; }
        public string AcademicYear { get; set; }

        // Helper properties
        public bool IsFiveTests => !T5.HasValue && !T6.HasValue;
        public bool IsSixTests => T5.HasValue || T6.HasValue;
        public bool HasAnyScore => (T1 ?? 0) > 0 || (T2 ?? 0) > 0 || (T3 ?? 0) > 0 ||
                                   (T4 ?? 0) > 0 || (T5 ?? 0) > 0 || (T6 ?? 0) > 0 || (Exam ?? 0) > 0;

        private const decimal PassMark = 50m;

        /// <summary>
        /// Validates all scores are within valid range (0-100)
        /// </summary>
        public (bool IsValid, List<string> Errors) ValidateScores()
        {
            var errors = new List<string>();
            var scores = new Dictionary<string, decimal?>
            {
                { "T1", T1 }, { "T2", T2 }, { "T3", T3 }, { "T4", T4 },
                { "T5", T5 }, { "T6", T6 }, { "Exam", Exam }
            };

            foreach (var score in scores)
            {
                if (score.Value.HasValue && (score.Value.Value < 0 || score.Value.Value > 100))
                {
                    errors.Add($"{score.Key} score {score.Value.Value} is invalid. Must be between 0 and 100.");
                }
            }

            return (errors.Count == 0, errors);
        }

        /// <summary>
        /// Main calculation entry point based on result type
        /// </summary>
        public void Calculate(string resultType, bool isSSS1Class = false)
        {
            if (!HasAnyScore) return;

            if (resultType == "SixTests" || resultType == "SixTests15")
            {
                CalculateSixTests(isSSS1Class);
            }
            else
            {
                CalculateFiveTests(isSSS1Class);
            }
        }

        /// <summary>
        /// Calculates values for Five Tests system
        /// </summary>
        public void CalculateFiveTests(bool isSSS1Class = false)
        {
            decimal t1 = T1 ?? 0;
            decimal t2 = T2 ?? 0;
            decimal t3 = T3 ?? 0;
            decimal t4 = T4 ?? 0;
            decimal exam = Exam ?? 0;

            if (isSSS1Class)
            {
                // SSS1: T1 and T2 are not taken, use (T3 + T4) * 0.3 for 60%
                Term1Mean = t3;
                Term2Mean = (t3 + t4) / 2;
                Percent60 = (t3 + t4) * 0.3m;
            }
            else
            {
                Term1Mean = (t1 + t2) / 2;
                Term2Mean = (t3 + t4) / 2;
                Percent60 = (t1 + t2 + t3 + t4) * 0.15m;
            }

            Percent40 = exam;
            FinalScore = Percent60 + Percent40;
            Grade = CalculateGrade(FinalScore);
            IsPassed = FinalScore >= PassMark;
            AnnualMean = FinalScore; // For consistency
        }

        /// <summary>
        /// Calculates values for Six Tests system
        /// </summary>
        public void CalculateSixTests(bool isSSS1Class = false)
        {
            decimal t1 = T1 ?? 0;
            decimal t2 = T2 ?? 0;
            decimal t3 = T3 ?? 0;
            decimal t4 = T4 ?? 0;
            decimal t5 = T5 ?? 0;
            decimal t6 = T6 ?? 0;

            if (isSSS1Class)
            {
                // SSS1: T1 and T2 not taken, use (T3 + T4) * 0.3 for 60%
                Term1Mean = t3;
                Term2Mean = (t3 + t4) / 2;
                Term3Mean = (t5 + t6) / 2;
                Percent60 = (t3 + t4) * 0.3m;
                Percent40 = t5 + t6;
                AnnualMean = (t3 + t4 + t5 + t6) / 4;
                FinalScore = Percent60 + Percent40;
            }
            else
            {
                Term1Mean = (t1 + t2) / 2;
                Term2Mean = (t3 + t4) / 2;
                Term3Mean = (t5 + t6) / 2;
                AnnualMean = (t1 + t2 + t3 + t4 + t5 + t6) / 6;
                FinalScore = AnnualMean;
            }

            Grade = CalculateGrade(FinalScore);
            IsPassed = FinalScore >= PassMark;
        }

        /// <summary>
        /// Legacy method - use Calculate() instead
        /// </summary>
        [Obsolete("Use Calculate() method instead")]
        public void CalculateFiveTests() => CalculateFiveTests(false);

        /// <summary>
        /// Legacy method - use Calculate() instead
        /// </summary>
        [Obsolete("Use Calculate() method instead")]
        public void CalculateSixTests() => CalculateSixTests(false);

        /// <summary>
        /// Legacy method - use Calculate() instead
        /// </summary>
        [Obsolete("Use Calculate() method instead")]
        public void CalculateSSS1FiveTests(bool includeT1 = true) => CalculateFiveTests(!includeT1);

        /// <summary>
        /// Legacy method - use Calculate() instead
        /// </summary>
        [Obsolete("Use Calculate() method instead")]
        public void CalculateSSS1SixTests() => CalculateSixTests(true);

        /// <summary>
        /// Calculates letter grade from score
        /// </summary>
        public static string CalculateGrade(decimal score)
        {
            if (score >= 80) return "A";
            if (score >= 70) return "B";
            if (score >= 60) return "C";
            if (score >= 50) return "D";
            if (score >= 40) return "E";
            return "F";
        }

        /// <summary>
        /// Calculates grade point from score (for transcript/CGPA)
        /// </summary>
        public static decimal CalculateGradePoint(decimal score)
        {
            if (score >= 80) return 4.0m;
            if (score >= 75) return 3.5m;
            if (score >= 70) return 3.0m;
            if (score >= 65) return 2.5m;
            if (score >= 60) return 2.0m;
            if (score >= 55) return 1.5m;
            if (score >= 50) return 1.0m;
            if (score >= 45) return 0.5m;
            return 0.0m;
        }

        /// <summary>
        /// Gets the number of tests entered
        /// </summary>
        public int GetEnteredTestCount()
        {
            int count = 0;
            if ((T1 ?? 0) > 0) count++;
            if ((T2 ?? 0) > 0) count++;
            if ((T3 ?? 0) > 0) count++;
            if ((T4 ?? 0) > 0) count++;
            if ((T5 ?? 0) > 0) count++;
            if ((T6 ?? 0) > 0) count++;
            if ((Exam ?? 0) > 0) count++;
            return count;
        }

        /// <summary>
        /// Gets all test scores as a list
        /// </summary>
        public List<decimal?> GetAllScores()
        {
            return new List<decimal?> { T1, T2, T3, T4, T5, T6, Exam };
        }

        /// <summary>
        /// Resets all calculated values
        /// </summary>
        public void ResetCalculations()
        {
            Term1Mean = 0;
            Term2Mean = 0;
            Term3Mean = 0;
            AnnualMean = 0;
            Percent60 = 0;
            Percent40 = 0;
            FinalScore = 0;
            Grade = null;
            IsPassed = false;
        }

        /// <summary>
        /// Creates a copy of this instance
        /// </summary>
        public SubjectScoreData Clone()
        {
            return new SubjectScoreData
            {
                SubjectId = this.SubjectId,
                SubjectName = this.SubjectName,
                IsCore = this.IsCore,
                DisplayOrder = this.DisplayOrder,
                T1 = this.T1,
                T2 = this.T2,
                T3 = this.T3,
                T4 = this.T4,
                T5 = this.T5,
                T6 = this.T6,
                Exam = this.Exam,
                Term1Mean = this.Term1Mean,
                Term2Mean = this.Term2Mean,
                Term3Mean = this.Term3Mean,
                AnnualMean = this.AnnualMean,
                Percent60 = this.Percent60,
                Percent40 = this.Percent40,
                FinalScore = this.FinalScore,
                Grade = this.Grade,
                IsPassed = this.IsPassed,
                StudentId = this.StudentId,
                Term = this.Term,
                AcademicYear = this.AcademicYear
            };
        }
    }
}