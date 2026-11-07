using System;
using System.Collections.Generic;
using System.Linq;
using SRMS.Desktop.Models;
using SRMS.Desktop.Helpers;

namespace SRMS.Desktop.Services
{
    public class PromotionEngine
    {
        private readonly ILogger _logger;

        private static readonly PromotionEngine _instance = new PromotionEngine();

        public static PromotionEngine Instance => _instance;

        public PromotionEngine()
        {
            _logger = Logger.Instance;
        }

        public static PromotionResult EvaluateStatic(ReportCardData reportData, SchoolPromotionCriteria criteria)
            => _instance.Evaluate(reportData, criteria);

        public static PromotionResult EvaluateStatic(ReportCardData reportData)
            => _instance.Evaluate(reportData);

        public static Dictionary<int, PromotionResult> BulkEvaluateStatic(List<ReportCardData> reports, SchoolPromotionCriteria criteria)
            => _instance.BulkEvaluate(reports, criteria);

        public static PromotionStatistics GetClassStatisticsStatic(List<ReportCardData> reports, SchoolPromotionCriteria criteria)
            => _instance.GetClassStatistics(reports, criteria);

        // ─────────────────────────────────────────────────────────────
        //  Batch fast-path — reads pass counts from ReportCardData only
        // ─────────────────────────────────────────────────────────────
        private int[] CalculateBatchPromotionWithCpp(List<ReportCardData> reports, SchoolPromotionCriteria criteria)
        {
            if (reports == null || reports.Count == 0) return new int[0];

            try
            {
                int count = reports.Count;

                double avgPassMark = (criteria != null && criteria.MinAveragePercentage.HasValue) ? (double)criteria.MinAveragePercentage.Value : 50.0;
                int minCoreRequired = (criteria != null && criteria.MinCoreSubjectsPassed.HasValue) ? criteria.MinCoreSubjectsPassed.Value : 3;
                int minOptRequired = (criteria != null && criteria.MinOptionalSubjectsPassed.HasValue) ? criteria.MinOptionalSubjectsPassed.Value : 3;
                int minTotalRequired = (criteria != null && criteria.MinTotalSubjectsPassed.HasValue) ? criteria.MinTotalSubjectsPassed.Value : 6;
                double minAttendance = (criteria != null && criteria.MinAttendancePercentage > 0) ? (double)criteria.MinAttendancePercentage : 75.0;
                bool includeAttendance = criteria != null && criteria.IncludeAttendance;

                string criteriaType = criteria != null ? (criteria.CriteriaType ?? string.Empty) : string.Empty;
                bool includeCore = criteriaType.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0;
                bool includeEnglishMaths = criteriaType.IndexOf("EnglishMaths", StringComparison.OrdinalIgnoreCase) >= 0
                                           || (criteria != null && criteria.RequireEnglishOrMaths);
                bool includeOptional = criteriaType.IndexOf("Optional", StringComparison.OrdinalIgnoreCase) >= 0;
                bool includeTotal = criteriaType.IndexOf("Total", StringComparison.OrdinalIgnoreCase) >= 0;

                int[] promotionStatus = new int[count];

                for (int i = 0; i < count; i++)
                {
                    var report = reports[i];
                    if (report == null) { promotionStatus[i] = 0; continue; }

                    bool promoted = true;

                    if ((double)report.AnnualAverage < avgPassMark) promoted = false;

                    if (includeCore && report.CoreSubjectsPassed < minCoreRequired) promoted = false;
                    if (includeOptional && report.OptionalSubjectsPassed < minOptRequired) promoted = false;
                    if (includeTotal && report.TotalSubjectsPassed < minTotalRequired) promoted = false;
                    if (includeEnglishMaths && !report.EnglishMathsPassed) promoted = false;

                    if (includeAttendance && report.Attendance != null && report.Attendance.SchoolDays > 0)
                    {
                        double att = (double)report.Attendance.DaysPresent / report.Attendance.SchoolDays * 100;
                        if (att < minAttendance) promoted = false;
                    }

                    promotionStatus[i] = promoted ? 1 : 0;
                }

                return promotionStatus;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Batch promotion error: {ex.Message}");
                return null;
            }
        }

        private string GetNextClass(string currentClass, bool isPromotedFlag)
        {
            if (!isPromotedFlag) return currentClass;
            if (string.IsNullOrEmpty(currentClass)) return currentClass;
            return ClassHelper.GetNextClass(currentClass);
        }

        // ─────────────────────────────────────────────────────────────
        //  Single evaluation — reads pass counts from ReportCardData only
        // ─────────────────────────────────────────────────────────────
        public PromotionResult Evaluate(ReportCardData reportData, SchoolPromotionCriteria criteria)
        {
            try
            {
                if (reportData == null)
                    return PromotionResult.NotPromoted("No data available", "NO DATA");

                if (criteria == null || criteria.Id == 0 || string.IsNullOrEmpty(criteria.CriteriaType))
                    criteria = PromotionCriteriaService.GetCurrentCriteria(reportData.ClassId);

                if (criteria == null || criteria.Id == 0 || string.IsNullOrEmpty(criteria.CriteriaType))
                {
                    bool defaultPromoted = reportData.AnnualAverage >= 50;
                    string defaultNextClass = defaultPromoted ? GetNextClass(reportData.CurrentClass, true) : GetNextClass(reportData.CurrentClass, false);
                    return defaultPromoted
                        ? PromotionResult.Promoted(defaultNextClass, "Default: Average >= 50%")
                        : PromotionResult.NotPromoted($"Default: Average {reportData.AnnualAverage:F1}% < 50%", "REPEAT");
                }

                var criteriaTypes = (criteria.CriteriaType ?? "")
                    .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(c => System.Text.RegularExpressions.Regex.Replace(c.Trim().ToLower(), "\\s+", ""))
                    .ToList();

                if (criteriaTypes.Count == 0)
                {
                    bool defaultPromoted = reportData.AnnualAverage >= 50;
                    string defaultNextClass = defaultPromoted ? GetNextClass(reportData.CurrentClass, true) : GetNextClass(reportData.CurrentClass, false);
                    return defaultPromoted
                        ? PromotionResult.Promoted(defaultNextClass, "No criteria - using default")
                        : PromotionResult.NotPromoted($"No criteria - avg {reportData.AnnualAverage:F1}% < 50%", "REPEAT");
                }

                bool isAverageSelected = criteriaTypes.Contains("average") || criteriaTypes.Contains("avg");
                bool isCoreSelected = criteriaTypes.Contains("coresubjects") || criteriaTypes.Contains("coresubject")
                                           || criteriaTypes.Contains("cores") || criteriaTypes.Contains("core");
                bool isEnglishMathsSelected = criteriaTypes.Contains("englishmaths") || criteriaTypes.Contains("englishmath")
                                           || criteria.RequireEnglishOrMaths;
                bool isOptionalSelected = criteriaTypes.Contains("optionalsubjects") || criteriaTypes.Contains("optionalsubject")
                                           || criteriaTypes.Contains("optional");
                bool isTotalSelected = criteriaTypes.Contains("totalsubjects") || criteriaTypes.Contains("totalsubject")
                                           || criteriaTypes.Contains("total") || criteriaTypes.Contains("totals");
                bool isAttendanceSelected = criteria.IncludeAttendance;

                int minCoreRequired = criteria.MinCoreSubjectsPassed ?? 3;
                int minOptRequired = criteria.MinOptionalSubjectsPassed ?? 3;
                int minTotalRequired = criteria.MinTotalSubjectsPassed ?? 6;
                double minAttendance = (double)(criteria.MinAttendancePercentage > 0 ? criteria.MinAttendancePercentage : 75);

                var reasons = new List<string>();
                var failedReasons = new List<string>();

                bool averagePassed = true, corePassed = true, englishMathsPassed = true,
                     optionalPassed = true, totalPassed = true, attendancePassed = true;

                // ── Average ──
                if (isAverageSelected)
                {
                    decimal minAvg = criteria.MinAveragePercentage ?? 50;
                    decimal studentAvg = reportData.AnnualAverage;
                    averagePassed = studentAvg >= minAvg;

                    if (averagePassed) reasons.Add($"✓ Average {studentAvg:F1}% ≥ {minAvg}%");
                    else failedReasons.Add($"✗ Average {studentAvg:F1}% < {minAvg}%");
                }

                // ── Core subjects — count comes from ReportDataService ──
                if (isCoreSelected)
                {
                    int actualCorePassed = reportData.CoreSubjectsPassed;
                    int totalCore = reportData.SubjectScores?.Count(s => s.IsCore) ?? 0;
                    corePassed = actualCorePassed >= minCoreRequired;

                    if (corePassed) reasons.Add($"✓ Core passed: {actualCorePassed}/{totalCore} (need ≥{minCoreRequired})");
                    else failedReasons.Add($"✗ Core passed: {actualCorePassed}/{totalCore} (need ≥{minCoreRequired})");
                }

                // ── English / Maths — boolean comes from ReportDataService ──
                if (isEnglishMathsSelected)
                {
                    englishMathsPassed = reportData.EnglishMathsPassed;
                    decimal emPassMark = criteria.EnglishMathsPassMark > 0 ? criteria.EnglishMathsPassMark : 50m;

                    if (englishMathsPassed) reasons.Add($"✓ English/Maths passed (≥{emPassMark}%)");
                    else failedReasons.Add($"✗ Neither English nor Maths ≥{emPassMark}%");
                }

                // ── Optional subjects — count comes from ReportDataService ──
                if (isOptionalSelected)
                {
                    int optPassed = reportData.OptionalSubjectsPassed;
                    optionalPassed = optPassed >= minOptRequired;

                    if (optionalPassed) reasons.Add($"✓ Optional passed: {optPassed} (need ≥{minOptRequired})");
                    else failedReasons.Add($"✗ Optional passed: {optPassed} (need ≥{minOptRequired})");
                }

                // ── Total subjects — count comes from ReportDataService ──
                if (isTotalSelected)
                {
                    int actualTotalPassed = reportData.TotalSubjectsPassed;
                    int totalSubjCount = reportData.SubjectScores?.Count ?? 0;
                    totalPassed = actualTotalPassed >= minTotalRequired;

                    if (totalPassed) reasons.Add($"✓ Total passed: {actualTotalPassed}/{totalSubjCount} (need ≥{minTotalRequired})");
                    else failedReasons.Add($"✗ Total passed: {actualTotalPassed}/{totalSubjCount} (need ≥{minTotalRequired})");
                }

                // ── Attendance ──
                if (isAttendanceSelected)
                {
                    decimal studentAttendance = 0;
                    if (reportData.Attendance != null && reportData.Attendance.SchoolDays > 0)
                        studentAttendance = (decimal)reportData.Attendance.DaysPresent / reportData.Attendance.SchoolDays * 100;

                    attendancePassed = studentAttendance >= (decimal)minAttendance;

                    if (attendancePassed) reasons.Add($"✓ Attendance {studentAttendance:F1}% ≥ {minAttendance}%");
                    else failedReasons.Add($"✗ Attendance {studentAttendance:F1}% < {minAttendance}%");
                }

                bool isPromoted = true;
                if (isAverageSelected && !averagePassed) isPromoted = false;
                if (isCoreSelected && !corePassed) isPromoted = false;
                if (isEnglishMathsSelected && !englishMathsPassed) isPromoted = false;
                if (isOptionalSelected && !optionalPassed) isPromoted = false;
                if (isTotalSelected && !totalPassed) isPromoted = false;
                if (isAttendanceSelected && !attendancePassed) isPromoted = false;

                string currentClass = reportData.CurrentClass ?? "";
                string nextClass = GetNextClass(currentClass, isPromoted);

                if (!isPromoted)
                {
                    string finalReason = failedReasons.Count > 0 ? string.Join("; ", failedReasons) : "Criteria not met";
                    return PromotionResult.NotPromoted(finalReason, "REPEAT");
                }

                string successReason = reasons.Count > 0 ? string.Join("; ", reasons) : "All criteria met";
                return PromotionResult.Promoted(nextClass, successReason);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error evaluating promotion");
                return PromotionResult.NotPromoted($"Error: {ex.Message}", "ERROR");
            }
        }

        public PromotionResult Evaluate(ReportCardData reportData)
        {
            var criteria = PromotionCriteriaService.GetCurrentCriteria(reportData?.ClassId);
            return Evaluate(reportData, criteria);
        }

        public Dictionary<int, PromotionResult> BulkEvaluate(List<ReportCardData> reports, SchoolPromotionCriteria criteria)
        {
            var results = new Dictionary<int, PromotionResult>();
            if (reports == null) return results;

            try
            {
                var promotionStatuses = CalculateBatchPromotionWithCpp(reports, criteria);
                if (promotionStatuses != null && promotionStatuses.Length == reports.Count)
                {
                    for (int i = 0; i < reports.Count; i++)
                    {
                        var report = reports[i];
                        if (report != null)
                        {
                            bool bulkPromoted = promotionStatuses[i] == 1;
                            string bulkNextClass = GetNextClass(report.CurrentClass, bulkPromoted);
                            results[report.StudentId] = bulkPromoted
                                ? PromotionResult.Promoted(bulkNextClass, "Batch evaluation")
                                : PromotionResult.NotPromoted("Batch evaluation", "REPEAT");
                        }
                    }
                    return results;
                }
            }
            catch { /* fall through */ }

            foreach (var report in reports)
                if (report != null)
                    results[report.StudentId] = Evaluate(report, criteria);

            return results;
        }

        public PromotionStatistics GetClassStatistics(List<ReportCardData> reports, SchoolPromotionCriteria criteria)
        {
            var stats = new PromotionStatistics();
            if (reports == null || reports.Count == 0) return stats;

            stats.TotalStudents = reports.Count;
            decimal totalAverageSum = 0;

            try
            {
                var promotionStatuses = CalculateBatchPromotionWithCpp(reports, criteria);
                if (promotionStatuses != null && promotionStatuses.Length == reports.Count)
                {
                    int promotedCount = 0;
                    for (int i = 0; i < reports.Count; i++)
                    {
                        var report = reports[i];
                        if (report != null)
                        {
                            if (promotionStatuses[i] == 1) promotedCount++;
                            totalAverageSum += report.AnnualAverage;
                        }
                    }
                    stats.PromotedCount = promotedCount;
                    stats.RepeatCount = stats.TotalStudents - promotedCount;
                    stats.AverageScore = stats.TotalStudents > 0 ? totalAverageSum / stats.TotalStudents : 0;
                    stats.TotalAverage = totalAverageSum;
                    stats.PromotionRate = stats.TotalStudents > 0 ? (double)promotedCount / stats.TotalStudents * 100 : 0;
                    return stats;
                }
            }
            catch { /* fall through */ }

            foreach (var report in reports)
            {
                if (report == null) continue;
                var result = Evaluate(report, criteria);
                if (result.IsPromoted) stats.PromotedCount++;
                else stats.RepeatCount++;
                totalAverageSum += report.AnnualAverage;
            }
            stats.AverageScore = stats.TotalStudents > 0 ? totalAverageSum / stats.TotalStudents : 0;
            stats.TotalAverage = totalAverageSum;
            stats.PromotionRate = stats.TotalStudents > 0 ? (double)stats.PromotedCount / stats.TotalStudents * 100 : 0;
            return stats;
        }
    }

    public class PromotionResult
    {
        public bool IsPromoted { get; set; }
        public string PromotionStatus { get; set; }
        public string NextClass { get; set; }
        public string Reason { get; set; }
        public string PrincipalComment { get; set; }

        public string PromotionDisplay => IsPromoted ? "PROMOTED" : "REPEAT";
        public string NextClassDisplay => NextClass ?? "N/A";

        public static PromotionResult Promoted(string nextClass, string reason = null)
            => new PromotionResult
            {
                IsPromoted = true,
                PromotionStatus = "PROMOTED",
                NextClass = nextClass,
                Reason = reason ?? "All criteria met",
                PrincipalComment = "CONGRATULATIONS ON YOUR PROMOTION!"
            };

        public static PromotionResult NotPromoted(string reason, string status = "REPEAT")
            => new PromotionResult
            {
                IsPromoted = false,
                PromotionStatus = status,
                NextClass = null,
                Reason = reason,
                PrincipalComment = "MORE EFFORT NEEDED. WORK HARDER NEXT TIME."
            };

        public string GetPrincipalComment() => PrincipalComment;
    }

    public class PromotionStatistics
    {
        public int TotalStudents { get; set; }
        public int PromotedCount { get; set; }
        public int RepeatCount { get; set; }
        public decimal AverageScore { get; set; }
        public double PromotionRate { get; set; }
        public decimal TotalAverage { get; set; }

        public string PromotionRateFormatted => $"{PromotionRate:F1}%";
        public string AverageScoreFormatted => $"{AverageScore:F1}%";
    }
}