using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using QRCoder;
using SRMS.Desktop.Abstractions;
using SRMS.Desktop.Data;
using SRMS.Desktop.Helpers;
using SRMS.Desktop.Models;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Parsing;
using Syncfusion.XlsIO;
using Syncfusion.XlsIORenderer;
using ImageFormat = SRMS.Desktop.Abstractions.ImageFormat;

namespace SRMS.Desktop.Services
{
    /// <summary>
    /// Generates a termly progressive report card on A4 portrait.
    /// </summary>
    public class TermlyReportGenerator : BaseReportGenerator
    {
        protected override string TemplateResourcePath
            => "SRMS.Desktop.Resources.TermlyProgressiveReportTemplate.xlsx";

        protected override string TemplateName => "TermlyProgressiveReportTemplate.xlsx";

        private const int QrAnchorRow = 10;
        private const int QrAnchorCol = 2;
        private const int QrSizePx = 96;

        private const int PhotoAnchorRow = 10;
        private const int PhotoAnchorCol = 16;

        private const string ParentMessageCell = "C42";
        private const string ClassTeacherNameCell = "C50";
        private const string PrincipalNameCell = "N50";
        private const string Term1AverageCell = "H34";
        private const string Term2AverageCell = "R34";
        private const string Term1GradeCell = "I34";
        private const string Term2GradeCell = "S34";

        private const int SubjectFirstRow = 20;
        private const int SubjectLastRow = 32;

        public TermlyReportGenerator(IImageProcessor images) : base(images) { }

        // ═══════════════════════════════════════════════════════════════
        //  Base class stubs — Termly reports don't use the annual layout
        // ═══════════════════════════════════════════════════════════════

        protected override void PopulateStudentInfo(IWorksheet ws, ReportCardData d, int rank, int classSize) { }
        protected override void PopulateSubjectScores(IWorksheet ws, ReportCardData d, List<Subject> activeSubjects, int startRow) { }
        protected override void PopulateBehaviourScores(IWorksheet ws, ReportCardData d) { }
        protected override void PopulatePsychomotorScores(IWorksheet ws, ReportCardData d) { }
        protected override void PopulateAttendance(IWorksheet ws, ReportCardData d, int studentId, string academicYear) { }
        protected override void PopulatePromotionInfo(IWorksheet ws, ReportCardData d, string teacherName, string displayClass) { }
        protected override void PopulateSubjectAnalysis(IWorksheet ws, ReportCardData d) { }

        // ═══════════════════════════════════════════════════════════════
        //  Utilities
        // ═══════════════════════════════════════════════════════════════

        private string GetGrade(decimal score)
        {
            if (score >= 80) return "A";
            if (score >= 70) return "B";
            if (score >= 60) return "C";
            if (score >= 50) return "D";
            if (score >= 40) return "E";
            return "F";
        }

        private static int[] ResolveTermsFromLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return new[] { 1 };
            string l = label.ToLowerInvariant();

            if (l.Contains("&") || l.Contains("and") || l.Contains("progressive"))
                return new[] { 1, 2 };

            if (l.Contains("1") || l.Contains("first") || l.Contains("one"))
                return new[] { 1 };

            if (l.Contains("2") || l.Contains("second") || l.Contains("two"))
                return new[] { 2 };

            return new[] { 1 };
        }

        private static string MapIntTermToLabel(int term)
        {
            switch (term)
            {
                case 1: return "Term 1";
                case 2: return "Term 2";
                case 3: return "Term 1 & 2";   // default "annual" for termly = both terms
                default: return "Term 1";
            }
        }

        private decimal ComputeTermMean(SubjectScoreData sc, int term)
        {
            if (sc == null) return 0m;
            if (term == 1)
            {
                decimal s = 0m; int n = 0;
                if (sc.T1.HasValue) { s += sc.T1.Value; n++; }
                if (sc.T2.HasValue) { s += sc.T2.Value; n++; }
                return n > 0 ? Math.Round(s / n, 1) : 0m;
            }
            else
            {
                decimal s = 0m; int n = 0;
                if (sc.T3.HasValue) { s += sc.T3.Value; n++; }
                if (sc.T4.HasValue) { s += sc.T4.Value; n++; }
                return n > 0 ? Math.Round(s / n, 1) : 0m;
            }
        }

        private decimal ComputeOverallTermAverageFromMarks(ReportCardData d, int term)
        {
            if (d?.SubjectScores == null || d.SubjectScores.Count == 0) return 0m;
            decimal total = 0m; int count = 0;
            foreach (var s in d.SubjectScores)
            {
                decimal mn = ComputeTermMean(s, term);
                if (mn > 0) { total += mn; count++; }
            }
            return count > 0 ? Math.Round(total / count, 1) : 0m;
        }

        private decimal ComputeTermAverageForSubject(SubjectScoreData sc, int term, bool sixTests)
        {
            var vals = new List<decimal>();
            if (term == 1)
            {
                if (sc.T1.HasValue) vals.Add(sc.T1.Value);
                if (sc.T2.HasValue) vals.Add(sc.T2.Value);
            }
            else if (term == 2)
            {
                if (sc.T3.HasValue) vals.Add(sc.T3.Value);
                if (sc.T4.HasValue) vals.Add(sc.T4.Value);
            }
            else
            {
                if (sixTests)
                {
                    if (sc.T5.HasValue) vals.Add(sc.T5.Value);
                    if (sc.T6.HasValue) vals.Add(sc.T6.Value);
                }
                else
                {
                    if (sc.Exam.HasValue) vals.Add(sc.Exam.Value);
                }
            }
            return vals.Count > 0 ? Math.Round(vals.Average(), 1) : 0m;
        }

        private async Task<decimal> ComputeStudentTermAverage(int studentId, int term, string academicYear)
        {
            try
            {
                var d = await _dataService.GetReportCardDataAsync(studentId, term, academicYear);
                if (d == null || d.SubjectScores == null || d.SubjectScores.Count == 0)
                    return 0m;

                bool sixTests = (Global.CurrentUser?.SchoolResultType ?? "FiveTests") == "SixTests";
                decimal total = 0m; int count = 0;
                foreach (var s in d.SubjectScores)
                {
                    total += ComputeTermAverageForSubject(s, term, sixTests);
                    count++;
                }
                return count > 0 ? Math.Round(total / count, 1) : 0m;
            }
            catch { return 0m; }
        }

        private async Task<int> ComputePositionInClass(int studentId, List<Student> classStudents, int term, string academicYear)
        {
            if (classStudents == null || classStudents.Count == 0) return 0;

            var averages = new List<KeyValuePair<int, decimal>>();
            foreach (var s in classStudents)
            {
                decimal avg = await ComputeStudentTermAverage(s.Id, term, academicYear);
                averages.Add(new KeyValuePair<int, decimal>(s.Id, avg));
            }
            var sorted = averages.OrderByDescending(x => x.Value).ToList();
            int idx = sorted.FindIndex(x => x.Key == studentId);
            return idx >= 0 ? idx + 1 : 0;
        }

        private string BuildParentNote(
            decimal term1Avg, decimal term2Avg,
            int term1Position, int term2Position,
            int totalTestsTaken, int totalTestsPassed, int totalTestsFailed,
            AttendanceData att1, AttendanceData att2)
        {
            int present = 0, totalDays = 0;
            if (att1 != null && att1.SchoolDays > 0) { present += att1.DaysPresent; totalDays += att1.SchoolDays; }
            if (att2 != null && att2.SchoolDays > 0) { present += att2.DaysPresent; totalDays += att2.SchoolDays; }
            decimal attendancePct = totalDays > 0 ? Math.Round((decimal)present / totalDays * 100m, 0) : 0m;

            decimal avg = term2Avg > 0 ? term2Avg : term1Avg;

            string trend;
            if (term1Avg > 0 && term2Avg > 0)
            {
                decimal delta = term2Avg - term1Avg;
                if (delta >= 3) trend = "improved";
                else if (delta <= -3) trend = "declined";
                else trend = "stable";
            }
            else trend = "single-term";

            int bestPos = term2Position > 0 ? term2Position : term1Position;

            string line1;
            if (avg >= 80) line1 = $"1) Your child has produced an outstanding academic performance this term, achieving an overall average of {avg:0.0}%.";
            else if (avg >= 70) line1 = $"1) Your child has achieved a very good academic result this term with an overall average of {avg:0.0}%.";
            else if (avg >= 60) line1 = $"1) Your child has achieved a good overall average of {avg:0.0}% this term and is making steady progress.";
            else if (avg >= 50) line1 = $"1) Your child has achieved a satisfactory overall average of {avg:0.0}% this term, which is a passing result.";
            else if (avg > 0) line1 = $"1) Your child's overall average of {avg:0.0}% this term indicates the need for focused academic support.";
            else line1 = "1) Sufficient academic results were not recorded this term to assess your child's overall performance.";

            string line2;
            if (trend == "improved") line2 = $"2) Performance improved from {term1Avg:0.0}% in Term 1 to {term2Avg:0.0}% in Term 2, and your child now stands at position {bestPos} in the class.";
            else if (trend == "declined") line2 = $"2) Performance declined from {term1Avg:0.0}% in Term 1 to {term2Avg:0.0}% in Term 2; your child currently stands at position {bestPos} in the class.";
            else if (trend == "stable") line2 = $"2) Performance has remained consistent across both terms, and your child maintains position {bestPos} in the class.";
            else if (term1Position > 0) line2 = $"2) Your child currently stands at position {term1Position} in the class this term.";
            else if (term2Position > 0) line2 = $"2) Your child currently stands at position {term2Position} in the class this term.";
            else line2 = "2) Class position information is not yet available for this term.";

            string line3;
            if (totalTestsTaken > 0)
            {
                decimal passRate = Math.Round((decimal)totalTestsPassed / totalTestsTaken * 100m, 0);
                line3 = $"3) Across {totalTestsTaken} assessments taken in the term papers, {totalTestsPassed} were passed ({passRate:0}% pass rate) and {totalTestsFailed} require improvement.";
            }
            else line3 = "3) No individual test papers have been recorded this term, so test-level analysis is not available.";

            string line4;
            if (attendancePct >= 90) line4 = $"4) Attendance has been excellent at {attendancePct:0}% ({present} of {totalDays} days), which strongly supports your child's learning.";
            else if (attendancePct >= 75) line4 = $"4) Attendance has been satisfactory at {attendancePct:0}% ({present} of {totalDays} days), and maintaining this will benefit your child.";
            else if (attendancePct > 0) line4 = $"4) Attendance stands at {attendancePct:0}% ({present} of {totalDays} days), which is below the recommended minimum of 75% and is affecting academic progress.";
            else line4 = "4) Attendance records for the term are incomplete, so we are unable to comment on regularity of attendance.";

            string line5;
            if (avg < 50 && attendancePct < 75)
                line5 = "5) We respectfully advise you to establish a fixed daily study routine, review homework together each evening, and ensure consistent school attendance so that your child can improve next term.";
            else if (avg < 50)
                line5 = "5) We recommend a fixed study schedule, daily revision of weak subjects, and closer follow-up on homework so that your child can raise performance next term.";
            else if (attendancePct < 75)
                line5 = "5) We strongly encourage you to prioritise regular school attendance, as consistent presence in class is essential for your child to reach full academic potential.";
            else if (avg < 70)
                line5 = "5) We encourage you to continue supporting your child's studies at home, and to focus especially on the subjects where the lowest marks were recorded.";
            else
                line5 = "5) We kindly ask you to continue nurturing this strong performance and to maintain the supportive home environment in which your child is thriving.";

            return $"{line1}\r\n{line2}\r\n{line3}\r\n{line4}\r\n{line5}";
        }

        private static string BuildSchoolLocation(School school)
        {
            if (school == null) return "";
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(school.District)) parts.Add(school.District);
            if (!string.IsNullOrWhiteSpace(school.Town)) parts.Add(school.Town);
            if (!string.IsNullOrWhiteSpace(school.Phone)) parts.Add("Tel: " + school.Phone);
            return string.Join(" | ", parts);
        }

        private static void WriteText(IWorksheet ws, string cellAddress, string value)
        {
            try { ws.Range[cellAddress].Text = value ?? ""; } catch { }
        }

        private static void ClearCell(IWorksheet ws, int row, int col)
        {
            try { ws.Range[row, col].Clear(); } catch { }
        }

        private static void WriteText(IWorksheet ws, int row, int col, string value)
        {
            try { ws.Range[row, col].Text = value ?? ""; } catch { }
        }

        private static void ForceCalculate(IWorksheet ws)
        {
            try { ws.EnableSheetCalculations(); } catch { }
            try { ws.Calculate(); } catch { }
        }

        private static decimal TryReadCellNumber(IWorksheet ws, string cellAddress)
        {
            try
            {
                var cell = ws.Range[cellAddress];
                if (cell == null) return 0m;

                try
                {
                    double n = cell.Number;
                    if (!double.IsNaN(n) && !double.IsInfinity(n))
                        return Convert.ToDecimal(n);
                }
                catch { }

                try
                {
                    object v2 = cell.Value2;
                    if (v2 != null)
                    {
                        if (v2 is double d && !double.IsNaN(d) && !double.IsInfinity(d)) return Convert.ToDecimal(d);
                        if (v2 is decimal m) return m;
                        if (decimal.TryParse(v2.ToString(), out var pv2)) return pv2;
                    }
                }
                catch { }

                try
                {
                    string t = cell.Text;
                    if (!string.IsNullOrWhiteSpace(t) && decimal.TryParse(t.Trim(), out var pt)) return pt;
                }
                catch { }

                try
                {
                    object v = cell.Value;
                    if (v != null)
                    {
                        if (v is double d2 && !double.IsNaN(d2) && !double.IsInfinity(d2)) return Convert.ToDecimal(d2);
                        if (v is decimal m2) return m2;
                        if (decimal.TryParse(v.ToString(), out var pvv)) return pvv;
                    }
                }
                catch { }
            }
            catch { }
            return 0m;
        }

        private static void ForceWriteNumber(IWorksheet ws, string cellAddress, double value)
        {
            try { ws.Range[cellAddress].Number = value; } catch { }
        }

        private static void ForceWriteText(IWorksheet ws, string cellAddress, string value)
        {
            try { ws.Range[cellAddress].Text = value ?? ""; } catch { }
        }

        // ═══════════════════════════════════════════════════════════════
        //  PUBLIC API — string-label variant (the real one)
        // ═══════════════════════════════════════════════════════════════

        public async Task<byte[]> GenerateTermlyReportAsync(
            int studentId, string termLabel, string academicYear,
            int classId = 0, List<Student> allStudentsInClass = null)
        {
            try
            {
                byte[] templateData = LoadTemplateFromResources();
                int[] selectedTerms = ResolveTermsFromLabel(termLabel);
                bool sixTests = (Global.CurrentUser?.SchoolResultType ?? "FiveTests") == "SixTests";

                var d = await _dataService.GetReportCardDataAsync(studentId, 1, academicYear);
                if (d == null) throw new Exception($"Failed to load data for student {studentId}");

                var school = _dbHelper.GetSchoolById(d.SchoolId);
                var sc = _dbHelper.GetStudentSpecialCase(studentId, academicYear);
                bool hasSC = sc != null && sc.IsActive;
                var sel = hasSC ? sc.GetSelectedTestsList() : null;
                bool showT1 = !hasSC || (sel != null && sel.Contains("T1"));
                bool showT2 = !hasSC || (sel != null && sel.Contains("T2"));
                bool showT3 = !hasSC || (sel != null && sel.Contains("T3"));
                bool showT4 = !hasSC || (sel != null && sel.Contains("T4"));
                bool isSSS1 = d.CurrentClass != null && d.CurrentClass.Trim().ToUpper().Contains("SSS I");

                if (allStudentsInClass == null && classId == 0)
                {
                    var s = _dbHelper.GetStudentById(studentId);
                    if (s != null) classId = s.ClassId;
                }
                if (allStudentsInClass == null && classId > 0)
                    allStudentsInClass = _dbHelper.GetStudents(d.SchoolId, classId);

                string teacherName = GetClassTeacherName(d.ClassId);
                string principalName = school?.PrincipalName ?? "Principal";

                var subjects = _dbHelper.GetSubjects(d.SchoolId, d.ClassId) ?? new List<Subject>();
                var activeSubjects = subjects.Where(s => s.IsActive).OrderBy(s => s.SubjectCode).ToList();

                var att1 = _dbHelper.GetAttendance(studentId, 1, academicYear);
                var att2 = _dbHelper.GetAttendance(studentId, 2, academicYear);

                int term1Pos = selectedTerms.Contains(1)
                    ? await ComputePositionInClass(studentId, allStudentsInClass, 1, academicYear) : 0;
                int term2Pos = selectedTerms.Contains(2)
                    ? await ComputePositionInClass(studentId, allStudentsInClass, 2, academicYear) : 0;

                using (var engine = new ExcelEngine())
                {
                    var app = engine.Excel;
                    app.DefaultVersion = ExcelVersion.Excel2016;

                    using (var ms = new MemoryStream(templateData))
                    {
                        var wb = app.Workbooks.Open(ms);
                        var ws = wb.Worksheets[0];

                        try
                        {
                            ws.PageSetup.Orientation = ExcelPageOrientation.Portrait;
                            ws.PageSetup.PaperSize = ExcelPaperSize.PaperA4;
                            ws.PageSetup.FitToPagesWide = 1;
                            ws.PageSetup.FitToPagesTall = 0;
                        }
                        catch { }

                        WriteText(ws, "A1", school?.SchoolName ?? d.SchoolName ?? "");
                        WriteText(ws, "D6", BuildSchoolLocation(school));

                        string reportMode = selectedTerms.Length == 2
                            ? "FIRST & SECOND TERM PROGRESSIVE REPORT"
                            : (selectedTerms[0] == 1 ? "FIRST TERM PROGRESSIVE REPORT" : "SECOND TERM PROGRESSIVE REPORT");
                        WriteText(ws, "E9", reportMode);
                        WriteText(ws, "G10", academicYear ?? "");

                        string sex = string.IsNullOrWhiteSpace(d.Gender) ? "N/A" : d.Gender;
                        string className = d.CurrentClass ?? "";
                        WriteText(ws, "F12", $"Student Name: {d.StudentName}, Sex: \"{sex}\"");
                        WriteText(ws, "F13", $"Admission Number: \"{d.AdmissionNumber}\"    Class: \"{className}\"");
                        WriteText(ws, "F14", $"Class Teacher: \"{teacherName}\"");
                        WriteText(ws, "E15", "");

                        var tempImages = new List<TempImage>();

                        var photo = InsertStudentPhoto(ws, d, PhotoAnchorRow, PhotoAnchorCol,
                            PhotoAnchorRow + 3, PhotoAnchorCol + 3);
                        if (photo != null) tempImages.Add(photo);

                        try
                        {
                            string qrPayload =
                                $"School: {school?.SchoolName ?? d.SchoolName}\n" +
                                $"Student: {d.StudentName}\n" +
                                $"Adm No: {d.AdmissionNumber}\n" +
                                $"Class: {d.CurrentClass}\n" +
                                $"Term: {reportMode}\n" +
                                $"Year: {academicYear}\n" +
                                $"Date: {DateTime.Now:dd/MM/yyyy}";

                            using (var qrGen = new QRCodeGenerator())
                            {
                                var qrData = qrGen.CreateQrCode(qrPayload, QRCodeGenerator.ECCLevel.H);
                                using (var qrCode = new BitmapByteQRCode(qrData))
                                {
                                    byte[] qrBytes = qrCode.GetGraphic(20);
                                    using (var qrMs = new MemoryStream(qrBytes))
                                    {
                                        var src = _images.LoadFromStream(qrMs);
                                        var resized = _images.Resize(src, QrSizePx, QrSizePx);
                                        using (var outStream = _images.ToStream(resized, ImageFormat.Png))
                                        {
                                            ws.Pictures.AddPicture(QrAnchorRow, QrAnchorCol, outStream, QrSizePx, QrSizePx);
                                        }
                                        tempImages.Add(new TempImage { Image = resized });
                                    }
                                }
                            }
                        }
                        catch (Exception ex) { _logger.LogError(ex, "QR error"); }

                        for (int i = 0; i < SubjectLastRow - SubjectFirstRow + 1; i++)
                        {
                            int row = SubjectFirstRow + i;
                            if (i >= activeSubjects.Count)
                            {
                                for (int c = 1; c <= 19; c++) ClearCell(ws, row, c);
                                continue;
                            }

                            var subj = activeSubjects[i];
                            var score = d.SubjectScores.FirstOrDefault(s => s.SubjectId == subj.Id);
                            SubjectScoreData working = score;
                            if (score != null && hasSC)
                            {
                                try
                                {
                                    var calc = _specialCaseService.CalculateScores(score, sc, sixTests, isSSS1);
                                    working = new SubjectScoreData
                                    {
                                        T1 = score.T1, T2 = score.T2, T3 = score.T3, T4 = score.T4, Exam = score.Exam,
                                        Term1Mean = calc.term1Mean, Term2Mean = calc.term2Mean,
                                        Percent60 = calc.percent60, FinalScore = calc.finalScore
                                    };
                                }
                                catch { working = score; }
                            }

                            if (selectedTerms.Contains(1))
                            {
                                WriteText(ws, row, 1, subj.SubjectName);
                                if (working != null)
                                {
                                    if (showT1 && working.T1.HasValue) ScoreColorCoding.ApplyToExcelCell(ws.Range[row, 6], working.T1.Value, ScoreType.TestScore);
                                    else ClearCell(ws, row, 6);
                                    if (showT2 && working.T2.HasValue) ScoreColorCoding.ApplyToExcelCell(ws.Range[row, 7], working.T2.Value, ScoreType.TestScore);
                                    else ClearCell(ws, row, 7);
                                }
                                else { ClearCell(ws, row, 6); ClearCell(ws, row, 7); }
                            }

                            if (selectedTerms.Contains(2))
                            {
                                WriteText(ws, row, 11, subj.SubjectName);
                                if (working != null)
                                {
                                    if (showT3 && working.T3.HasValue) ScoreColorCoding.ApplyToExcelCell(ws.Range[row, 16], working.T3.Value, ScoreType.TestScore);
                                    else ClearCell(ws, row, 16);
                                    if (showT4 && working.T4.HasValue) ScoreColorCoding.ApplyToExcelCell(ws.Range[row, 17], working.T4.Value, ScoreType.TestScore);
                                    else ClearCell(ws, row, 17);
                                }
                                else { ClearCell(ws, row, 16); ClearCell(ws, row, 17); }
                            }
                        }

                        WriteText(ws, "L38", term1Pos > 0 ? $"{term1Pos}{GetOrdinalSuffix(term1Pos)}" : "");
                        WriteText(ws, "L39", term2Pos > 0 ? $"{term2Pos}{GetOrdinalSuffix(term2Pos)}" : "");

                        if (att1 != null && att1.SchoolDays > 0) WriteText(ws, "F40", $"{att1.DaysPresent}/{att1.SchoolDays}");
                        if (att2 != null && att2.SchoolDays > 0) WriteText(ws, "R40", $"{att2.DaysPresent}/{att2.SchoolDays}");

                        ForceCalculate(ws);

                        decimal h34 = TryReadCellNumber(ws, Term1AverageCell);
                        decimal r34 = TryReadCellNumber(ws, Term2AverageCell);
                        if (h34 <= 0) h34 = ComputeOverallTermAverageFromMarks(d, 1);
                        if (r34 <= 0) r34 = ComputeOverallTermAverageFromMarks(d, 2);

                        if (h34 > 0)
                        {
                            ForceWriteNumber(ws, Term1AverageCell, Convert.ToDouble(h34));
                            ForceWriteText(ws, Term1GradeCell, GetGrade(h34));
                        }
                        else { ClearCell(ws, 34, 8); ClearCell(ws, 34, 9); }

                        if (r34 > 0)
                        {
                            ForceWriteNumber(ws, Term2AverageCell, Convert.ToDouble(r34));
                            ForceWriteText(ws, Term2GradeCell, GetGrade(r34));
                        }
                        else { ClearCell(ws, 34, 18); ClearCell(ws, 34, 19); }

                        int testsTaken = 0, testsPassed = 0, testsFailed = 0;
                        if (d.SubjectScores != null)
                        {
                            foreach (var s in d.SubjectScores)
                            {
                                foreach (var v in new[] { s.T1, s.T2, s.T3, s.T4 })
                                {
                                    if (v.HasValue)
                                    {
                                        testsTaken++;
                                        if (v.Value >= 50) testsPassed++;
                                        else testsFailed++;
                                    }
                                }
                            }
                        }

                        string note = BuildParentNote(h34, r34, term1Pos, term2Pos,
                            testsTaken, testsPassed, testsFailed, att1, att2);
                        WriteText(ws, ParentMessageCell, note);

                        WriteText(ws, ClassTeacherNameCell, teacherName);
                        WriteText(ws, PrincipalNameCell, principalName);

                        ForceCalculate(ws);

                        var renderer = new XlsIORenderer();
                        var pdfDoc = new PdfDocument();
                        pdfDoc.PageSettings.Orientation = PdfPageOrientation.Portrait;
                        pdfDoc.PageSettings.Size = PdfPageSize.A4;
                        pdfDoc.PageSettings.Margins.All = 0;

                        var settings = new XlsIORendererSettings { LayoutOptions = LayoutOptions.FitSheetOnOnePage };

                        using (PdfDocument convertedDoc = renderer.ConvertToPDF(wb, settings))
                        {
                            for (int i = 0; i < convertedDoc.Pages.Count; i++)
                            {
                                PdfPage newPage = pdfDoc.Pages.Add();
                                newPage.Graphics.DrawPdfTemplate(
                                    convertedDoc.Pages[i].CreateTemplate(),
                                    new Syncfusion.Drawing.PointF(0, 0));
                            }
                        }

                        using (var outStream = new MemoryStream())
                        {
                            pdfDoc.Save(outStream);
                            byte[] bytes = outStream.ToArray();
                            foreach (var ti in tempImages)
                            {
                                try { ti?.Image?.Dispose(); } catch { }
                                try { ti?.Stream?.Dispose(); } catch { }
                            }
                            return bytes;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error generating termly report for {studentId}");
                throw;
            }
        }

        public async Task<byte[]> GenerateSelectedTermlyReportsAsync(
            List<int> studentIds, string termLabel, string academicYear,
            IProgress<int> progress = null, IProgress<string> statusProgress = null)
        {
            if (studentIds == null || studentIds.Count == 0) return new byte[0];
            var tempFiles = new List<string>();
            try
            {
                for (int i = 0; i < studentIds.Count; i++)
                {
                    string tmp = Path.GetTempFileName() + ".pdf";
                    var pdf = await GenerateTermlyReportAsync(studentIds[i], termLabel, academicYear);
                    File.WriteAllBytes(tmp, pdf);
                    tempFiles.Add(tmp);
                    progress?.Report(((i + 1) * 100) / studentIds.Count);
                    statusProgress?.Report($"Generated {i + 1} of {studentIds.Count}");
                }

                if (tempFiles.Count == 1) return File.ReadAllBytes(tempFiles[0]);

                using (var finalDoc = new PdfDocument())
                {
                    foreach (var f in tempFiles)
                    {
                        using (var loaded = new PdfLoadedDocument(f))
                        {
                            for (int i = 0; i < loaded.Pages.Count; i++)
                            {
                                var lp = loaded.Pages[i];
                                finalDoc.PageSettings.Width = lp.Size.Width;
                                finalDoc.PageSettings.Height = lp.Size.Height;
                                finalDoc.PageSettings.Margins.All = 0;
                                var np = finalDoc.Pages.Add();
                                np.Graphics.DrawPdfTemplate(lp.CreateTemplate(), new Syncfusion.Drawing.PointF(0, 0));
                            }
                        }
                    }
                    using (var ms = new MemoryStream())
                    {
                        finalDoc.Save(ms);
                        return ms.ToArray();
                    }
                }
            }
            finally
            {
                foreach (var f in tempFiles) { try { File.Delete(f); } catch { } }
            }
        }

        public bool SaveTermlyReportToFile(int studentId, string termLabel, string academicYear, string filePath)
        {
            try
            {
                var bytes = GenerateTermlyReportAsync(studentId, termLabel, academicYear)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                File.WriteAllBytes(filePath, bytes);
                return true;
            }
            catch (Exception ex) { _logger.LogError(ex, "Failed to save termly report"); return false; }
        }

        // ═══════════════════════════════════════════════════════════════
        //  Base class abstract overrides — thin wrappers that map int→label
        // ═══════════════════════════════════════════════════════════════

        public override Task<byte[]> GenerateReportCardAsync(
            int studentId, int term, string academicYear,
            int classId = 0, List<Student> allStudentsInClass = null)
        {
            string label = MapIntTermToLabel(term);
            return GenerateTermlyReportAsync(studentId, label, academicYear, classId, allStudentsInClass);
        }

        public override Task<byte[]> GenerateSelectedReportCardsAsync(
            List<int> studentIds, int term, string academicYear,
            IProgress<int> progress = null, IProgress<string> statusProgress = null)
        {
            string label = MapIntTermToLabel(term);
            return GenerateSelectedTermlyReportsAsync(studentIds, label, academicYear, progress, statusProgress);
        }

        public override Task<bool> SaveReportCardToFileAsync(
            int studentId, int term, string academicYear, string filePath)
        {
            string label = MapIntTermToLabel(term);
            try
            {
                var bytes = GenerateTermlyReportAsync(studentId, label, academicYear)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                File.WriteAllBytes(filePath, bytes);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save termly report");
                return Task.FromResult(false);
            }
        }

        public override bool SaveReportCardToFile(
            int studentId, int term, string academicYear, string filePath)
        {
            string label = MapIntTermToLabel(term);
            return SaveTermlyReportToFile(studentId, label, academicYear, filePath);
        }
    }
}