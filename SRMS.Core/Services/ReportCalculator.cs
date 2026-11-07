using System;
using System.Collections.Generic;
using System.Linq;

namespace SRMS.Desktop.Services
{
    public class ReportCalculator
    {
        private readonly ILogger _logger;

        public ReportCalculator()
        {
            _logger = Logger.Instance;
        }

        public decimal CalculateTermMean(decimal? test1, decimal? test2)
        {
            decimal t1 = test1 ?? 0;
            decimal t2 = test2 ?? 0;
            return (t1 + t2) / 2;
        }

        public decimal CalculatePercent60(decimal? t1, decimal? t2, decimal? t3, decimal? t4)
        {
            decimal total = (t1 ?? 0) + (t2 ?? 0) + (t3 ?? 0) + (t4 ?? 0);
            return total * 0.15m;
        }

        public decimal CalculateFinalScore(decimal percent60, decimal? exam)
        {
            return percent60 + (exam ?? 0);
        }

        public decimal CalculateTotal(List<decimal> values)
        {
            if (values == null || values.Count == 0) return 0;
            return values.Sum();
        }

        public decimal CalculateAverage(List<decimal> values)
        {
            if (values == null || values.Count == 0) return 0;
            return values.Average();
        }

        public decimal CalculateBehaviourTermAverage(params int[] ratings)
        {
            if (ratings == null || ratings.Length == 0) return 0;
            return (decimal)ratings.Average();
        }

        public decimal CalculateBehaviourFinalAverage(decimal term1Avg, decimal term2Avg, decimal term3Avg)
        {
            return (term1Avg + term2Avg + term3Avg) / 3;
        }

        public string GetBehaviourGrade(decimal average)
        {
            if (average >= 4.5m) return "Excellent";
            if (average >= 3.5m) return "Good";
            if (average >= 2.5m) return "Fair";
            if (average >= 1.5m) return "Poor";
            return "Very Poor";
        }

        public string GetLetterGrade(decimal score)
        {
            if (score >= 80) return "A";
            if (score >= 70) return "B";
            if (score >= 60) return "C";
            if (score >= 50) return "D";
            if (score >= 40) return "E";
            return "F";
        }

        public string GetLetterGradeWithPlusMinus(decimal score)
        {
            string grade = GetLetterGrade(score);
            decimal remainder = score % 10;

            if (grade == "A" && remainder >= 5) return "A+";
            if (grade == "A") return "A";
            if (grade == "B" && remainder >= 5) return "B+";
            if (grade == "B" && remainder <= 2) return "B-";
            if (grade == "C" && remainder >= 5) return "C+";
            if (grade == "C" && remainder <= 2) return "C-";
            if (grade == "D" && remainder >= 5) return "D+";
            if (grade == "D" && remainder <= 2) return "D-";

            return grade;
        }

        public bool IsSubjectPassed(decimal score, decimal passMark)
        {
            return score >= passMark;
        }

        public decimal CalculateAttendancePercentage(int daysPresent, int schoolDays)
        {
            if (schoolDays <= 0) return 0;
            return (decimal)daysPresent / schoolDays * 100;
        }

        public string GetAttendanceStatus(decimal percentage)
        {
            if (percentage >= 90) return "Excellent";
            if (percentage >= 75) return "Good";
            if (percentage >= 60) return "Fair";
            if (percentage >= 50) return "Poor";
            return "Very Poor";
        }

        public int CountTestsPassed(List<decimal?> scores, decimal passMark)
        {
            if (scores == null) return 0;
            return scores.Count(s => (s ?? 0) >= passMark);
        }

        public int CountTestsTaken(List<decimal?> scores)
        {
            if (scores == null) return 0;
            return scores.Count(s => s.HasValue && s.Value > 0);
        }

        public decimal CalculateSubjectMeanForTerm(List<decimal?> testScores, bool isSixTestsSystem = false, int termNumber = 1)
        {
            if (testScores == null || testScores.Count == 0) return 0;

            if (isSixTestsSystem)
            {
                // Six Tests: Two tests per term
                int test1Index = (termNumber - 1) * 2;
                int test2Index = test1Index + 1;

                decimal test1 = testScores.Count > test1Index && testScores[test1Index].HasValue ? testScores[test1Index].Value : 0;
                decimal test2 = testScores.Count > test2Index && testScores[test2Index].HasValue ? testScores[test2Index].Value : 0;

                return (test1 + test2) / 2;
            }
            else
            {
                // Five Tests: Two tests per term for Terms 1 and 2, Exam for Term 3
                if (termNumber == 3)
                {
                    decimal exam = testScores.Count > 0 && testScores[0].HasValue ? testScores[0].Value : 0;
                    return exam;
                }
                else
                {
                    int test1Index = (termNumber - 1) * 2;
                    int test2Index = test1Index + 1;

                    decimal test1 = testScores.Count > test1Index && testScores[test1Index].HasValue ? testScores[test1Index].Value : 0;
                    decimal test2 = testScores.Count > test2Index && testScores[test2Index].HasValue ? testScores[test2Index].Value : 0;

                    return (test1 + test2) / 2;
                }
            }
        }

        public int CalculateRank(int studentId, List<KeyValuePair<int, decimal>> studentScores)
        {
            if (studentScores == null || studentScores.Count == 0) return 0;

            // Competition ranking: equal scores receive the same rank; next rank skips accordingly.
            var ranked = studentScores.OrderByDescending(x => x.Value).ToList();
            int assignedRank = 0;

            for (int i = 0; i < ranked.Count; i++)
            {
                // round to 1 decimal place for comparison to avoid tiny precision differences
                decimal currentScore = Math.Round(ranked[i].Value, 1);

                if (i == 0)
                {
                    assignedRank = 1;
                }
                else
                {
                    decimal prevScore = Math.Round(ranked[i - 1].Value, 1);
                    if (currentScore == prevScore)
                    {
                        // same assignedRank as previous
                    }
                    else
                    {
                        // position is index + 1 (so ties cause skipped ranks)
                        assignedRank = i + 1;
                    }
                }

                if (ranked[i].Key == studentId)
                    return assignedRank;
            }

            return 0;
        }

        public string GetOrdinalSuffix(int number)
        {
            if (number <= 0) return "";
            if (number % 100 >= 11 && number % 100 <= 13) return "th";

            switch (number % 10)
            {
                case 1: return "st";
                case 2: return "nd";
                case 3: return "rd";
                default: return "th";
            }
        }

        public string GetPromotionMessage(bool isPromoted, string nextClass)
        {
            if (isPromoted)
                return $"PROMOTED TO {nextClass}";
            else
                return $"REPEAT {nextClass}";
        }

        public string GetPrincipalComment(bool isPromoted, decimal annualAverage)
        {
            if (isPromoted)
            {
                if (annualAverage >= 80)
                    return "EXCELLENT PERFORMANCE! KEEP UP THE GOOD WORK!";
                else if (annualAverage >= 70)
                    return "VERY GOOD! PROUD OF YOUR ACHIEVEMENT.";
                else if (annualAverage >= 60)
                    return "GOOD JOB! CONTINUE WORKING HARD.";
                else
                    return "A JOB WELL DONE, CONGRATULATIONS";
            }
            else
            {
                if (annualAverage >= 45)
                    return "MORE EFFORT NEEDED. YOU CAN DO BETTER.";
                else
                    return "IMPROVEMENT NEEDED. WORK HARDER NEXT TIME.";
            }
        }

        public decimal CalculateClassAverage(List<decimal> studentAverages)
        {
            if (studentAverages == null || studentAverages.Count == 0) return 0;
            return studentAverages.Average();
        }

        public int CountStudentsAboveThreshold(List<decimal> studentAverages, decimal threshold)
        {
            if (studentAverages == null) return 0;
            return studentAverages.Count(a => a >= threshold);
        }

        public decimal CalculatePassRate(List<decimal> studentAverages, decimal passMark)
        {
            if (studentAverages == null || studentAverages.Count == 0) return 0;
            int passed = studentAverages.Count(a => a >= passMark);
            return (decimal)passed / studentAverages.Count * 100;
        }
    }
}