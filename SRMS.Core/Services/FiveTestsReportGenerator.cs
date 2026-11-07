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
using Syncfusion.Pdf.Graphics;
using Syncfusion.Pdf.Parsing;
using Syncfusion.XlsIO;
using Syncfusion.XlsIORenderer;
using ImageFormat = SRMS.Desktop.Abstractions.ImageFormat;

namespace SRMS.Desktop.Services
{
    public class FiveTestsReportGenerator : BaseReportGenerator
    {
        protected override string TemplateResourcePath
            => "SRMS.Desktop.Resources.FiveTestsReportCardTemplate.xlsx";

        protected override string TemplateName => "FiveTestsReportCardTemplate.xlsx";

        public FiveTestsReportGenerator(IImageProcessor images) : base(images) { }

        private string GetGrade(decimal score)
        {
            if (score >= 80) return "A";
            if (score >= 70) return "B";
            if (score >= 60) return "C";
            if (score >= 50) return "D";
            if (score >= 40) return "E";
            return "F";
        }

        protected override void PopulateStudentInfo(IWorksheet ws, ReportCardData d, int rank, int classSize)
        {
            ws.Range[1, 1].Text = d.StudentName ?? "";
            ws.Range[1, 12].Text = d.CurrentClass ?? "";
            ws.Range[2, 12].Text = d.AdmissionNumber ?? "";
            ws.Range[3, 4].Text = d.DateOfBirth.ToString("dd/MM/yyyy");
            ws.Range[3, 11].Text = d.AcademicYear ?? "";
            if (rank > 0) ws.Range[3, 25].Text = $"{rank}{GetOrdinalSuffix(rank)}";
            if (classSize > 0) ws.Range[2, 26].Number = classSize;
        }

        protected override void PopulateSubjectScores(IWorksheet ws, ReportCardData d, List<Subject> activeSubjects, int startRow)
        {
            var specialCaseConfig = _dbHelper.GetStudentSpecialCase(d.StudentId, d.AcademicYear);
            bool hasSpecialCase = specialCaseConfig != null && specialCaseConfig.IsActive;
            var selectedTests = hasSpecialCase ? specialCaseConfig.GetSelectedTestsList() : null;
            bool showT1 = !hasSpecialCase || (selectedTests != null && selectedTests.Contains("T1"));
            bool showT2 = !hasSpecialCase || (selectedTests != null && selectedTests.Contains("T2"));
            bool showT3 = !hasSpecialCase || (selectedTests != null && selectedTests.Contains("T3"));
            bool showT4 = !hasSpecialCase || (selectedTests != null && selectedTests.Contains("T4"));
            bool showExam = !hasSpecialCase || (selectedTests != null && selectedTests.Contains("Exam"));
            bool isSSS1 = d.CurrentClass != null && d.CurrentClass.Trim().ToUpper().Contains("SSS I");
            bool scMN1T2 = hasSpecialCase && specialCaseConfig.Term1Formula == "T2";
            bool scP60 = hasSpecialCase && specialCaseConfig.Percent60Formula == "(T2+T3+T4)*0.2";

            int row = startRow;
            foreach (var subject in activeSubjects)
            {
                var raw = d.SubjectScores.FirstOrDefault(s => s.SubjectId == subject.Id);
                SubjectScoreData w = null;
                if (raw != null)
                {
                    w = new SubjectScoreData
                    {
                        SubjectId = raw.SubjectId,
                        T1 = raw.T1, T2 = raw.T2, T3 = raw.T3, T4 = raw.T4, Exam = raw.Exam,
                        Term1Mean = raw.Term1Mean, Term2Mean = raw.Term2Mean,
                        Percent60 = raw.Percent60, FinalScore = raw.FinalScore, Grade = raw.Grade
                    };
                }

                if (hasSpecialCase && raw != null)
                {
                    var calc = _specialCaseService.CalculateScores(raw, specialCaseConfig, false, isSSS1);
                    if (w != null)
                    {
                        w.Term1Mean = calc.term1Mean;
                        w.Term2Mean = calc.term2Mean;
                        w.Percent60 = calc.percent60;
                        w.FinalScore = calc.finalScore;
                        w.Grade = GetGrade(calc.finalScore);
                    }
                }

                ws.Range[row, 1].Text = subject.SubjectName;

                if (w != null)
                {
                    var cT1 = ws.Range[row, 5];
                    if (showT1 && w.T1.HasValue) ScoreColorCoding.ApplyToExcelCell(cT1, w.T1.Value, ScoreType.TestScore);
                    else cT1.Clear();

                    var cT2 = ws.Range[row, 6];
                    if (showT2 && w.T2.HasValue) ScoreColorCoding.ApplyToExcelCell(cT2, w.T2.Value, ScoreType.TestScore);
                    else cT2.Clear();

                    var cMN1 = ws.Range[row, 7];
                    decimal mn1;
                    if (scMN1T2) mn1 = w.T2 ?? 0;
                    else if (hasSpecialCase && !string.IsNullOrEmpty(specialCaseConfig.Term1Formula))
                    {
                        var ev = new ExpressionEvaluator();
                        ev.SetVariable("T1", w.T1 ?? 0);
                        ev.SetVariable("T2", w.T2 ?? 0);
                        mn1 = ev.Evaluate(specialCaseConfig.Term1Formula);
                    }
                    else mn1 = w.Term1Mean;
                    ScoreColorCoding.ApplyToExcelCell(cMN1, mn1, ScoreType.MeanScore);

                    var cT3 = ws.Range[row, 8];
                    if (showT3 && w.T3.HasValue) ScoreColorCoding.ApplyToExcelCell(cT3, w.T3.Value, ScoreType.TestScore);
                    else cT3.Clear();

                    var cT4 = ws.Range[row, 9];
                    if (showT4 && w.T4.HasValue) ScoreColorCoding.ApplyToExcelCell(cT4, w.T4.Value, ScoreType.TestScore);
                    else cT4.Clear();

                    var cMN2 = ws.Range[row, 10];
                    decimal mn2;
                    if (hasSpecialCase && !string.IsNullOrEmpty(specialCaseConfig.Term2Formula))
                    {
                        var ev = new ExpressionEvaluator();
                        ev.SetVariable("T3", w.T3 ?? 0);
                        ev.SetVariable("T4", w.T4 ?? 0);
                        mn2 = ev.Evaluate(specialCaseConfig.Term2Formula);
                    }
                    else mn2 = w.Term2Mean;
                    ScoreColorCoding.ApplyToExcelCell(cMN2, mn2, ScoreType.MeanScore);

                    var cP60 = ws.Range[row, 11];
                    decimal p60;
                    if (scP60) p60 = ((w.T2 ?? 0) + (w.T3 ?? 0) + (w.T4 ?? 0)) * 0.2m;
                    else if (hasSpecialCase && !string.IsNullOrEmpty(specialCaseConfig.Percent60Formula))
                    {
                        var ev = new ExpressionEvaluator();
                        ev.SetVariable("T1", w.T1 ?? 0);
                        ev.SetVariable("T2", w.T2 ?? 0);
                        ev.SetVariable("T3", w.T3 ?? 0);
                        ev.SetVariable("T4", w.T4 ?? 0);
                        p60 = ev.Evaluate(specialCaseConfig.Percent60Formula);
                    }
                    else p60 = w.Percent60;
                    ScoreColorCoding.ApplyToExcelCell(cP60, p60, ScoreType.Percent60);

                    var cExam = ws.Range[row, 12];
                    if (showExam && w.Exam.HasValue) ScoreColorCoding.ApplyToExcelCell(cExam, w.Exam.Value, ScoreType.Percent40);
                    else cExam.Clear();

                    var cFinal = ws.Range[row, 13];
                    decimal fs;
                    if (hasSpecialCase && !string.IsNullOrEmpty(specialCaseConfig.FinalScoreFormula))
                    {
                        var ev = new ExpressionEvaluator();
                        ev.SetVariable("Percent60", p60);
                        ev.SetVariable("Exam", w.Exam ?? 0);
                        fs = ev.Evaluate(specialCaseConfig.FinalScoreFormula);
                    }
                    else fs = w.FinalScore;
                    ScoreColorCoding.ApplyToExcelCell(cFinal, fs, ScoreType.MeanScore);

                    var cGrade = ws.Range[row, 14];
                    string g = GetGrade(fs);
                    if (!string.IsNullOrEmpty(g))
                    {
                        cGrade.Text = g;
                        cGrade.CellStyle.Font.RGBColor = (fs < 50 && fs > 0)
                            ? Syncfusion.Drawing.Color.Red
                            : Syncfusion.Drawing.Color.Black;
                    }
                    else cGrade.Clear();
                }
                else
                {
                    for (int c = 5; c <= 14; c++) ws.Range[row, c].Clear();
                }
                row++;
            }
        }

        protected override void PopulateBehaviourScores(IWorksheet ws, ReportCardData d)
        {
            var b = d.BehaviourScores;
            if (b == null) return;
            var rows = new (int r, int t1a, int t1b, int t2a, int t2b, int t3a, int t3b)[]
            {
                (12, b.Term1ClassParticipationT1, b.Term1ClassParticipationT2, b.Term2ClassParticipationT1, b.Term2ClassParticipationT2, b.Term3ClassParticipationT1, b.Term3ClassParticipationT2),
                (13, b.Term1HonestyT1, b.Term1HonestyT2, b.Term2HonestyT1, b.Term2HonestyT2, b.Term3HonestyT1, b.Term3HonestyT2),
                (14, b.Term1EffortT1, b.Term1EffortT2, b.Term2EffortT1, b.Term2EffortT2, b.Term3EffortT1, b.Term3EffortT2),
                (15, b.Term1SelfDisciplineT1, b.Term1SelfDisciplineT2, b.Term2SelfDisciplineT1, b.Term2SelfDisciplineT2, b.Term3SelfDisciplineT1, b.Term3SelfDisciplineT2),
                (16, b.Term1PunctualityT1, b.Term1PunctualityT2, b.Term2PunctualityT1, b.Term2PunctualityT2, b.Term3PunctualityT1, b.Term3PunctualityT2),
                (17, b.Term1SchoolInvolvementT1, b.Term1SchoolInvolvementT2, b.Term2SchoolInvolvementT1, b.Term2SchoolInvolvementT2, b.Term3SchoolInvolvementT1, b.Term3SchoolInvolvementT2),
                (18, b.Term1NeatnessT1, b.Term1NeatnessT2, b.Term2NeatnessT1, b.Term2NeatnessT2, b.Term3NeatnessT1, b.Term3NeatnessT2),
            };
            foreach (var x in rows)
            {
                ws.Range[x.r, 20].Number = x.t1a;
                ws.Range[x.r, 21].Number = x.t1b;
                ws.Range[x.r, 23].Number = x.t2a;
                ws.Range[x.r, 24].Number = x.t2b;
                ws.Range[x.r, 26].Number = x.t3a;
                ws.Range[x.r, 27].Number = x.t3b;
            }
        }

        protected override void PopulatePsychomotorScores(IWorksheet ws, ReportCardData d)
        {
            var p = d.PsychomotorScores;
            if (p == null) return;
            var rows = new (int r, int t1a, int t1b, int t2a, int t2b, int t3a, int t3b)[]
            {
                (23, p.Term1IndividualSportsT1, p.Term1IndividualSportsT2, p.Term2IndividualSportsT1, p.Term2IndividualSportsT2, p.Term3IndividualSportsT1, p.Term3IndividualSportsT2),
                (24, p.Term1TeamPlayerT1, p.Term1TeamPlayerT2, p.Term2TeamPlayerT1, p.Term2TeamPlayerT2, p.Term3TeamPlayerT1, p.Term3TeamPlayerT2),
                (25, p.Term1AthleticsT1, p.Term1AthleticsT2, p.Term2AthleticsT1, p.Term2AthleticsT2, p.Term3AthleticsT1, p.Term3AthleticsT2),
                (26, p.Term1DrawingT1, p.Term1DrawingT2, p.Term2DrawingT1, p.Term2DrawingT2, p.Term3DrawingT1, p.Term3DrawingT2),
                (27, p.Term1HandwritingT1, p.Term1HandwritingT2, p.Term2HandwritingT1, p.Term2HandwritingT2, p.Term3HandwritingT1, p.Term3HandwritingT2),
            };
            foreach (var x in rows)
            {
                ws.Range[x.r, 20].Number = x.t1a;
                ws.Range[x.r, 21].Number = x.t1b;
                ws.Range[x.r, 23].Number = x.t2a;
                ws.Range[x.r, 24].Number = x.t2b;
                ws.Range[x.r, 26].Number = x.t3a;
                ws.Range[x.r, 27].Number = x.t3b;
            }
        }

        protected override void PopulateAttendance(IWorksheet ws, ReportCardData d, int studentId, string academicYear)
        {
            if (d.Attendance == null) return;
            var t1 = _dbHelper.GetAttendance(studentId, 1, academicYear);
            var t2 = _dbHelper.GetAttendance(studentId, 2, academicYear);
            var t3 = d.Attendance;

            if (t1 != null && t1.Id > 0)
            {
                ws.Range[25, 4].Number = t1.DaysPresent;
                ws.Range[26, 4].Number = t1.DaysAbsent;
            }
            if (t2 != null && t2.Id > 0)
            {
                ws.Range[25, 6].Number = t2.DaysPresent;
                ws.Range[26, 6].Number = t2.DaysAbsent;
            }
            ws.Range[25, 8].Number = t3.DaysPresent;
            ws.Range[26, 8].Number = t3.DaysAbsent;
        }

        protected override void PopulatePromotionInfo(IWorksheet ws, ReportCardData d, string teacherName, string displayClass)
        {
            ws.Range[31, 4].Text = GetClassTeacherComment(d.AnnualAverage);
            ws.Range[29, 5].Text = teacherName;

            bool promoted = d.PromotionResult?.IsPromoted ?? (d.AnnualAverage >= 50);
            string currentClass = d.CurrentClass ?? "";
            string nextClass = promoted ? ClassHelper.GetNextClass(currentClass) : displayClass;
            string promoText = promoted ? $"PROMOTED TO {nextClass}" : $"REPEAT {displayClass}";

            ws.Range[34, 15].Text = promoText;
            ws.Range[31, 15].Text = GetPrincipalComment(promoted, d.AnnualAverage);
            ws.Range[33, 15].Text = d.PrincipalName ?? "Principal";
        }

        protected override void PopulateSubjectAnalysis(IWorksheet ws, ReportCardData d)
        {
            ws.Range[35, 5].Number = d.CoreSubjectsPassed;
            ws.Range[35, 12].Number = d.OptionalSubjectsPassed;
            ws.Range[36, 5].Number = d.TotalSubjectsPassed;
            ws.Range[36, 12].Number = Convert.ToDouble(d.AnnualAverage);
        }

        public override async Task<byte[]> GenerateReportCardAsync(int studentId, int term, string academicYear, int classId = 0, List<Student> allStudentsInClass = null)
        {
            try
            {
                byte[] templateData = LoadTemplateFromResources();
                var d = await _dataService.GetReportCardDataAsync(studentId, term, academicYear);
                if (d == null) throw new Exception($"Failed to load data for student {studentId}");

                if (allStudentsInClass == null && classId == 0)
                {
                    var s = _dbHelper.GetStudentById(studentId);
                    if (s != null) classId = s.ClassId;
                }
                if (allStudentsInClass == null && classId > 0)
                    allStudentsInClass = _dbHelper.GetStudents(d.SchoolId, classId);

                int rank = await CalculateStudentRank(studentId, allStudentsInClass, term, academicYear);
                int classSize = allStudentsInClass?.Count ?? 0;
                string teacherName = GetClassTeacherName(d.ClassId);
                string displayClass = d.CurrentClass ?? "";

                var subjects = _dbHelper.GetSubjects(d.SchoolId, d.ClassId) ?? new List<Subject>();
                var activeSubjects = subjects.Where(s => s.IsActive).OrderBy(s => s.SubjectCode).ToList();

                using (var engine = new ExcelEngine())
                {
                    var app = engine.Excel;
                    app.DefaultVersion = ExcelVersion.Excel2016;

                    using (var ms = new MemoryStream(templateData))
                    {
                        var wb = app.Workbooks.Open(ms);
                        var ws = wb.Worksheets[0];

                        SetWorksheetPageOrientation(ws);
                        PopulateStudentInfo(ws, d, rank, classSize);
                        PopulateSubjectScores(ws, d, activeSubjects, 9);
                        PopulateBehaviourScores(ws, d);
                        PopulatePsychomotorScores(ws, d);
                        PopulateAttendance(ws, d, studentId, academicYear);
                        PopulatePromotionInfo(ws, d, teacherName, displayClass);
                        PopulateSubjectAnalysis(ws, d);

                        var tempImages = new List<TempImage>();
                        try
                        {
                            string rankText = $"{rank}{GetOrdinalSuffix(rank)}";
                            bool promoted = d.PromotionResult?.IsPromoted ?? (d.AnnualAverage >= 50);
                            string nextClass = promoted ? ClassHelper.GetNextClass(d.CurrentClass ?? "") : displayClass;
                            string promoText = promoted ? $"PROMOTED TO {nextClass}" : $"REPEAT {displayClass}";
                            string qrData =
                                $"School: {d.SchoolName}\n" +
                                $"Student Name: {d.StudentName}\n" +
                                $"AD No.: {d.AdmissionNumber}\n" +
                                $"Status: {promoText}\n" +
                                $"Position: {rankText}\n" +
                                $"Academic Year: {d.AcademicYear}\n" +
                                $"Annual Average: {d.AnnualAverage:F1}\n" +
                                $"Date Printed: {DateTime.Now:dd/MM/yyyy}\n" +
                                $"Software Used: School Result Management System (SRMS) Developed By: Alimamy Amara Sumah – Kambia, Sierra Leone.";

                            using (var qrGen = new QRCodeGenerator())
                            {
                                var qrCodeData = qrGen.CreateQrCode(qrData, QRCodeGenerator.ECCLevel.Q);
                                using (var qrCode = new BitmapByteQRCode(qrCodeData))
                                {
                                    byte[] qrBytes = qrCode.GetGraphic(20);
                                    using (var qrMs = new MemoryStream(qrBytes))
                                    {
                                        var src = _images.LoadFromStream(qrMs);
                                        var resized = _images.Resize(src, 114, 110);
                                        using (var outStream = _images.ToStream(resized, ImageFormat.Png))
                                        {
                                            ws.Pictures.AddPicture(32, 26, outStream, 114, 110);
                                        }
                                        tempImages.Add(new TempImage { Image = resized });
                                    }
                                }
                            }
                        }
                        catch (Exception ex) { _logger.LogError(ex, "QR Code error"); }

                        var photo = InsertStudentPhoto(ws, d, 3, 26, 9, 29);
                        if (photo != null) tempImages.Add(photo);

                        try { ws.Calculate(); } catch { }
                        ws.EnableSheetCalculations();

                        var renderer = new XlsIORenderer();

                        var pdfDoc = new PdfDocument();
                        pdfDoc.PageSettings.Orientation = PdfPageOrientation.Landscape;
                        pdfDoc.PageSettings.Size = PdfPageSize.A4;
                        pdfDoc.PageSettings.Margins.All = 0;

                        var settings = new XlsIORendererSettings
                        {
                            LayoutOptions = LayoutOptions.FitSheetOnOnePage
                        };

                        using (PdfDocument convertedDoc = renderer.ConvertToPDF(wb, settings))
                        {
                            for (int i = 0; i < convertedDoc.Pages.Count; i++)
                            {
                                PdfPage newPage = pdfDoc.Pages.Add();
                                newPage.Graphics.DrawPdfTemplate(
                                    convertedDoc.Pages[i].CreateTemplate(),
                                    new Syncfusion.Drawing.PointF(0, 0));
                            }

                            try
                            {
                                string headerText = null;
                                var name = d.SchoolName?.Trim();
                                if (!string.IsNullOrEmpty(name))
                                {
                                    var parts = new List<string> { name };
                                    var loc = new List<string>();
                                    if (!string.IsNullOrEmpty(d.SchoolDistrict)) loc.Add(d.SchoolDistrict.Trim());
                                    if (!string.IsNullOrEmpty(d.SchoolTown)) loc.Add(d.SchoolTown.Trim());
                                    if (loc.Count > 0) parts.Add(string.Join(", ", loc));
                                    headerText = string.Join(" - ", parts);
                                }

                                if (!string.IsNullOrEmpty(headerText))
                                {
                                    var font = new PdfStandardFont(PdfFontFamily.Helvetica, 12, PdfFontStyle.Bold);
                                    var brush = PdfBrushes.Black;
                                    var fmt = new PdfStringFormat
                                    {
                                        Alignment = PdfTextAlignment.Center,
                                        LineAlignment = PdfVerticalAlignment.Top
                                    };
                                    foreach (PdfPage page in pdfDoc.Pages)
                                    {
                                        try
                                        {
                                            var pageWidth = page.GetClientSize().Width;
                                            var bounds = new Syncfusion.Drawing.RectangleF(0, 8, pageWidth, 30);
                                            page.Graphics.DrawString(headerText, font, brush, bounds, fmt);
                                        }
                                        catch { }
                                    }
                                }
                            }
                            catch (Exception ex) { _logger.LogError(ex, "Header draw error"); }
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
                _logger.LogError(ex, $"Error generating report card for student {studentId}");
                throw;
            }
        }

        public override async Task<byte[]> GenerateSelectedReportCardsAsync(List<int> studentIds, int term, string academicYear, IProgress<int> progress = null, IProgress<string> statusProgress = null)
        {
            if (studentIds == null || studentIds.Count == 0) return new byte[0];

            var tempFiles = new List<string>();
            try
            {
                for (int i = 0; i < studentIds.Count; i++)
                {
                    string tmp = Path.GetTempFileName() + ".pdf";
                    var pdf = await GenerateReportCardAsync(studentIds[i], term, academicYear);
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

        public override async Task<bool> SaveReportCardToFileAsync(int studentId, int term, string academicYear, string filePath)
        {
            try
            {
                var pdf = await GenerateReportCardAsync(studentId, term, academicYear);
                File.WriteAllBytes(filePath, pdf);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving report card to {filePath}");
                return false;
            }
        }

        public override bool SaveReportCardToFile(int studentId, int term, string academicYear, string filePath)
        {
            try
            {
                var pdf = GenerateReportCardAsync(studentId, term, academicYear)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                File.WriteAllBytes(filePath, pdf);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving report card to {filePath}");
                return false;
            }
        }
    }
}