using System.Collections.Generic;

namespace SRMS.Desktop.Models
{
    public static class BECEConstants
    {
        public static readonly Dictionary<int, string> GradeWordMap = new Dictionary<int, string>
        {
            { 1, "ONE" },
            { 2, "TWO" },
            { 3, "THREE" },
            { 4, "FOUR" },
            { 5, "FIVE" },
            { 6, "SIX" },
            { 7, "SEVEN" }
        };

        public static readonly Dictionary<int, string> InterpretationMap = new Dictionary<int, string>
        {
            { 1, "EXCELLENT" },
            { 2, "VERY GOOD" },
            { 3, "GOOD" },
            { 4, "CREDIT" },
            { 5, "CREDIT" },
            { 6, "PASS" },
            { 7, "FAIL" }
        };

        public static readonly Dictionary<int, string> OverallPerformanceMap = new Dictionary<int, string>
        {
            { 6, "DISTINCTION" },
            { 12, "MERIT" },
            { 18, "CREDIT" },
            { 24, "PASS" },
            { 30, "PASS" },
            { 99, "FAIL" }
        };

        public static string GetGradeWord(int score)
        {
            return GradeWordMap.TryGetValue(score, out var word) ? word : "---";
        }

        public static string GetInterpretation(int score)
        {
            return InterpretationMap.TryGetValue(score, out var interp) ? interp : "---";
        }

        public static string GetOverallPerformance(int aggregate)
        {
            if (aggregate <= 6) return "DISTINCTION";
            if (aggregate <= 12) return "MERIT";
            if (aggregate <= 18) return "CREDIT";
            if (aggregate <= 24) return "PASS";
            if (aggregate <= 30) return "PASS";
            return "FAIL";
        }

        public static string GetGradeClass(int score)
        {
            return $"grade-{score}";
        }
    }
}