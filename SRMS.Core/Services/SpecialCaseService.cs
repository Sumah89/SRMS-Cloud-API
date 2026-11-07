using SRMS.Desktop.Data;
using SRMS.Desktop.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace SRMS.Desktop.Services
{
    public class SpecialCaseService : IDisposable
    {
        private readonly DatabaseHelper _dbHelper;
        private readonly ExpressionEvaluator _evaluator;
        private bool _disposed = false;

        // Canonical variable names used by the evaluator
        public static readonly string[] AvailableVariables = new[]
        {
            "T1", "T2", "T3", "T4", "T5", "T6", "Exam",
            "Term1Mean", "Term2Mean", "Term3Mean",
            "MN1", "MN2", "MN3", "MN",
            "Percent40", "Percent60",
            "AnnualMean", "FinalScore"
        };

        public SpecialCaseService()
        {
            _dbHelper = new DatabaseHelper();
            _evaluator = new ExpressionEvaluator();
        }

        public StudentSpecialCase GetSpecialCaseConfig(int studentId, string academicYear)
            => _dbHelper.GetStudentSpecialCase(studentId, academicYear);

        public bool IsSpecialCaseActive(int studentId, string academicYear)
        {
            var config = GetSpecialCaseConfig(studentId, academicYear);
            return config != null && config.IsActive;
        }

        public List<string> GetAvailableTestsForStudent(int studentId, string academicYear, string resultType)
        {
            var config = GetSpecialCaseConfig(studentId, academicYear);
            if (config == null || !config.IsActive)
            {
                return resultType == "SixTests"
                    ? new List<string> { "T1", "T2", "T3", "T4", "T5", "T6" }
                    : new List<string> { "T1", "T2", "T3", "T4", "Exam" };
            }
            return config.GetSelectedTestsList();
        }

        // =====================================================================
        // PUBLIC API – 6-value result (preferred)
        // =====================================================================
        public (decimal term1Mean, decimal term2Mean, decimal term3Mean, decimal percent60,
                decimal annualMean, decimal finalScore)
            CalculateScores(SubjectScoreData rawScores, StudentSpecialCase config, bool isSixTests)
        {
            if (rawScores == null) rawScores = new SubjectScoreData();
            config = config ?? new StudentSpecialCase();

            // Reset any previous state
            foreach (var v in AvailableVariables)
                _evaluator.SetVariable(v, 0m);

            // Populate raw test scores (nulls become 0)
            SetVar("T1", rawScores.T1);
            SetVar("T2", rawScores.T2);
            SetVar("T3", rawScores.T3);
            SetVar("T4", rawScores.T4);
            SetVar("T5", rawScores.T5);
            SetVar("T6", rawScores.T6);
            SetVar("Exam", rawScores.Exam);

            // Determine which tests were taken
            var selectedTests = config.GetSelectedTestsList();
            if (selectedTests == null || selectedTests.Count == 0)
            {
                selectedTests = isSixTests
                    ? new List<string> { "T1", "T2", "T3", "T4", "T5", "T6" }
                    : new List<string> { "T1", "T2", "T3", "T4", "Exam" };
            }

            bool showT1 = Has(selectedTests, "T1") && !config.IsSSSI;
            bool showT2 = Has(selectedTests, "T2") && !config.IsSSSI;
            bool showT3 = Has(selectedTests, "T3");
            bool showT4 = Has(selectedTests, "T4");
            bool showT5 = Has(selectedTests, "T5");
            bool showT6 = Has(selectedTests, "T6");
            bool showExam = Has(selectedTests, "Exam");
            bool isSSSI = config.IsSSSI;

            // If SSSI is active, force T1/T2 to zero
            if (isSSSI)
            {
                _evaluator.SetVariable("T1", 0m);
                _evaluator.SetVariable("T2", 0m);
                rawScores.T1 = 0m;
                rawScores.T2 = 0m;
            }

            // ============ TERM 1 MEAN (MN1) ============
            decimal term1Mean = 0m;
            if (showT1 || showT2)
            {
                string formula = NormalizeFormula(config.Term1Formula);
                if (string.IsNullOrWhiteSpace(formula))
                    formula = BuildDefaultTerm1(showT1, showT2, isSSSI);
                term1Mean = SafeEvaluate(formula, BuildDefaultTerm1(showT1, showT2, isSSSI));
            }
            SetVar("Term1Mean", term1Mean);
            SetVar("MN1", term1Mean);

            // ============ TERM 2 MEAN (MN2) ============
            decimal term2Mean = 0m;
            if (showT3 || showT4)
            {
                string formula = NormalizeFormula(config.Term2Formula);
                if (string.IsNullOrWhiteSpace(formula))
                    formula = BuildDefaultTerm2(showT3, showT4);
                term2Mean = SafeEvaluate(formula, BuildDefaultTerm2(showT3, showT4));
            }
            SetVar("Term2Mean", term2Mean);
            SetVar("MN2", term2Mean);

            // ============ TERM 3 MEAN (MN3) ============
            decimal term3Mean = 0m;
            if (isSixTests)
            {
                if (showT5 || showT6)
                {
                    string formula = NormalizeFormula(config.Term3Formula);
                    if (string.IsNullOrWhiteSpace(formula))
                        formula = BuildDefaultTerm3SixTests(showT5, showT6);
                    term3Mean = SafeEvaluate(formula, BuildDefaultTerm3SixTests(showT5, showT6));
                }
            }
            else
            {
                if (showExam)
                {
                    string formula = NormalizeFormula(config.Term3Formula);
                    if (string.IsNullOrWhiteSpace(formula))
                        formula = "Exam";
                    term3Mean = SafeEvaluate(formula, "Exam");
                }
            }
            SetVar("Term3Mean", term3Mean);
            SetVar("MN3", term3Mean);

            // ============ PERCENT60 (Five-Tests only) ============
            decimal percent60 = 0m;
            if (!isSixTests)
            {
                string formula = NormalizeFormula(config.Percent60Formula);
                if (string.IsNullOrWhiteSpace(formula))
                    formula = BuildDefaultPercent60(showT1, showT2, showT3, showT4, isSSSI);
                percent60 = SafeEvaluate(formula, BuildDefaultPercent60(showT1, showT2, showT3, showT4, isSSSI));
            }
            decimal percent40 = 0m; // Not calculated here; Exam is separate in Five-Tests
            SetVar("Percent60", percent60);
            SetVar("Percent40", percent40);

            // ============ ANNUAL MEAN (MN) ============
            string annualFormula = NormalizeFormula(config.AnnualMeanFormula);
            string annualDefault = isSixTests
                ? BuildDefaultAnnualSixTests(showT1, showT2, showT3, showT4, showT5, showT6, isSSSI)
                : "(MN1+MN2)/2";
            if (string.IsNullOrWhiteSpace(annualFormula))
                annualFormula = annualDefault;
            decimal annualMean = SafeEvaluate(annualFormula, annualDefault);
            SetVar("AnnualMean", annualMean);
            SetVar("MN", annualMean);

            // ============ FINAL SCORE ============
            string finalFormula = NormalizeFormula(config.FinalScoreFormula);
            string finalDefault = isSixTests ? "AnnualMean" : "AnnualMean + Exam";
            if (string.IsNullOrWhiteSpace(finalFormula))
                finalFormula = finalDefault;
            decimal finalScore = SafeEvaluate(finalFormula, finalDefault);

            // Clamp to 0..100 for safety
            term1Mean = Clamp(term1Mean);
            term2Mean = Clamp(term2Mean);
            term3Mean = Clamp(term3Mean);
            percent60 = Clamp(percent60);
            annualMean = Clamp(annualMean);
            finalScore = Clamp(finalScore);

            return (term1Mean, term2Mean, term3Mean, percent60, annualMean, finalScore);
        }

        /// <summary>
        /// Legacy 5-value overload kept for backward compatibility.
        /// </summary>
        public (decimal term1Mean, decimal term2Mean, decimal term3Mean, decimal percent60, decimal finalScore)
            CalculateScores(SubjectScoreData rawScores, StudentSpecialCase config, bool isSixTests, bool isSSSI)
        {
            if (config == null) config = new StudentSpecialCase();
            if (isSSSI) config.IsSSSI = true;

            var r = CalculateScores(rawScores, config, isSixTests);
            return (r.term1Mean, r.term2Mean, r.term3Mean, r.percent60, r.finalScore);
        }

        public SubjectScoreData ApplySpecialCaseToScore(SubjectScoreData rawScore, StudentSpecialCase config, bool isSixTests)
        {
            if (config == null || !config.IsActive) return rawScore;

            var c = CalculateScores(rawScore, config, isSixTests);
            rawScore.Term1Mean = c.term1Mean;
            rawScore.Term2Mean = c.term2Mean;
            rawScore.Term3Mean = c.term3Mean;
            rawScore.Percent60 = c.percent60;
            rawScore.AnnualMean = c.annualMean;
            rawScore.FinalScore = c.finalScore;
            rawScore.Grade = GetGrade(c.finalScore);
            rawScore.IsPassed = c.finalScore >= 50m;
            return rawScore;
        }

        public List<SubjectScoreData> ApplySpecialCaseToAllScores(List<SubjectScoreData> scores, StudentSpecialCase config, bool isSixTests)
        {
            if (config == null || !config.IsActive) return scores;
            var result = new List<SubjectScoreData>();
            foreach (var s in scores) result.Add(ApplySpecialCaseToScore(s, config, isSixTests));
            return result;
        }

        // =====================================================================
        // HELPERS
        // =====================================================================

        private void SetVar(string name, decimal? value)
        {
            try { _evaluator.SetVariable(name, value ?? 0m); }
            catch { /* ignore unknown variables */ }
        }

        private static bool Has(List<string> list, string name) =>
            list != null && list.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));

        private static decimal Clamp(decimal v)
        {
            if (v < 0m) return 0m;
            if (v > 100m) return 100m;
            return v;
        }

        /// <summary>
        /// Attempts to evaluate a formula. If it fails, returns the fallback
        /// evaluated instead. This keeps the pipeline running even with bad user input.
        /// </summary>
        private decimal SafeEvaluate(string formula, string fallbackFormula)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(formula))
                    return _evaluator.Evaluate(fallbackFormula ?? "0");
                return _evaluator.Evaluate(formula);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SpecialCaseService] Formula failed: '{formula}' – {ex.Message}");
                try { return _evaluator.Evaluate(fallbackFormula ?? "0"); }
                catch { return 0m; }
            }
        }

        /// <summary>
        /// Normalizes a user-entered formula:
        ///   - Trims whitespace.
        ///   - Converts '40%' or '60%' into 'Percent40' / 'Percent60'.
        ///   - Collapses 'MN 1' / 'MN1' into 'MN1'.
        ///   - Makes variables case-insensitive (evaluator handles the actual variable table).
        /// </summary>
        private static string NormalizeFormula(string formula)
        {
            if (string.IsNullOrWhiteSpace(formula)) return formula;

            string s = formula.Trim();

            // "40%" or "40 %" → Percent40 ; "60%" → Percent60
            s = Regex.Replace(s, @"(\d+(?:\.\d+)?)\s*%", match =>
            {
                string num = match.Groups[1].Value;
                if (num == "40") return "Percent40";
                if (num == "60") return "Percent60";
                return $"({num}/100)";
            });

            // Spaced variants → single token
            s = Regex.Replace(s, @"\bMN\s+1\b", "MN1", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bMN\s+2\b", "MN2", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bMN\s+3\b", "MN3", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bTerm\s+1\s+Mean\b", "Term1Mean", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bTerm\s+2\s+Mean\b", "Term2Mean", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bTerm\s+3\s+Mean\b", "Term3Mean", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bAnnual\s+Mean\b", "AnnualMean", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bFinal\s+Score\b", "FinalScore", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bPercent\s+40\b", "Percent40", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bPercent\s+60\b", "Percent60", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bExam\s+Score\b", "Exam", RegexOptions.IgnoreCase);

            return s;
        }

        private static string BuildDefaultTerm1(bool showT1, bool showT2, bool isSSSI)
        {
            if (isSSSI) return "0";
            if (showT1 && showT2) return "(T1+T2)/2";
            if (showT1) return "T1";
            if (showT2) return "T2";
            return "0";
        }

        private static string BuildDefaultTerm2(bool showT3, bool showT4)
        {
            if (showT3 && showT4) return "(T3+T4)/2";
            if (showT3) return "T3";
            if (showT4) return "T4";
            return "0";
        }

        private static string BuildDefaultTerm3SixTests(bool showT5, bool showT6)
        {
            if (showT5 && showT6) return "(T5+T6)/2";
            if (showT5) return "T5";
            if (showT6) return "T6";
            return "0";
        }

        private static string BuildDefaultPercent60(bool showT1, bool showT2, bool showT3, bool showT4, bool isSSSI)
        {
            if (isSSSI)
            {
                if (showT3 && showT4) return "(T3+T4)*0.3";
                if (showT3) return "T3*0.6";
                if (showT4) return "T4*0.6";
                return "0";
            }

            int count = 0;
            string terms = "";
            if (showT1) { count++; terms += "T1+"; }
            if (showT2) { count++; terms += "T2+"; }
            if (showT3) { count++; terms += "T3+"; }
            if (showT4) { count++; terms += "T4+"; }

            if (count == 0) return "0";
            terms = terms.TrimEnd('+');
            decimal w = 0.6m / count;
            return $"({terms})*{w.ToString(CultureInfo.InvariantCulture)}";
        }

        private static string BuildDefaultAnnualSixTests(bool s1, bool s2, bool s3, bool s4, bool s5, bool s6, bool isSSSI)
        {
            int count = 0;
            string terms = "";
            if (s1 && !isSSSI) { count++; terms += "T1+"; }
            if (s2 && !isSSSI) { count++; terms += "T2+"; }
            if (s3) { count++; terms += "T3+"; }
            if (s4) { count++; terms += "T4+"; }
            if (s5) { count++; terms += "T5+"; }
            if (s6) { count++; terms += "T6+"; }

            if (count == 0) return "0";
            terms = terms.TrimEnd('+');
            return $"({terms})/{count}";
        }

        private string GetGrade(decimal score)
        {
            if (score >= 80) return "A";
            if (score >= 70) return "B";
            if (score >= 60) return "C";
            if (score >= 50) return "D";
            if (score >= 40) return "E";
            return "F";
        }

        // =====================================================================
        // CRUD helpers
        // =====================================================================
        public List<StudentSpecialCase> GetAllSpecialCasesForSchool(int schoolId)
            => _dbHelper.GetAllStudentSpecialCases(schoolId);

        public List<StudentSpecialCase> GetSpecialCasesForClass(int schoolId, int classId)
        {
            var all = GetAllSpecialCasesForSchool(schoolId);
            var studentsInClass = _dbHelper.GetStudents(schoolId, classId);
            var ids = studentsInClass.Select(s => s.Id).ToHashSet();
            return all.Where(sc => ids.Contains(sc.StudentId)).ToList();
        }

        public bool DeleteSpecialCaseConfig(int studentId, string academicYear)
            => _dbHelper.DeleteStudentSpecialCase(studentId, academicYear);

        public bool DeleteSpecialCaseConfigPermanently(int studentId, string academicYear)
            => _dbHelper.DeleteStudentSpecialCasePermanently(studentId, academicYear);

        public bool SaveSpecialCaseConfig(StudentSpecialCase config)
            => _dbHelper.SaveStudentSpecialCase(config);

        public bool HasActiveSpecialCase(int studentId, string academicYear)
        {
            var c = GetSpecialCaseConfig(studentId, academicYear);
            return c != null && c.IsActive;
        }

        public int GetActiveSpecialCaseCount(int schoolId, string academicYear)
        {
            var all = GetAllSpecialCasesForSchool(schoolId);
            return all.Count(sc => sc.IsActive &&
                string.Equals(sc.AcademicYear, academicYear, StringComparison.OrdinalIgnoreCase));
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _dbHelper?.Dispose();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}