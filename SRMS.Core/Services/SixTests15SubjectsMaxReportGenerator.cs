using System;
using System.Collections.Generic;
using System.Linq;
using SRMS.Desktop.Abstractions;
using SRMS.Desktop.Helpers;
using SRMS.Desktop.Models;
using Syncfusion.XlsIO;

namespace SRMS.Desktop.Services
{
    public sealed class SixTests15SubjectsMaxReportGenerator : SixTestsReportGenerator
    {
        private const int SubjectStartRow15 = 9;
        private const int SubjectEndRow = 23;
        private const int TotalRow = 24;
        private const int AverageRow = 25;

        public SixTests15SubjectsMaxReportGenerator(IImageProcessor images) : base(images) { }

        protected override string TemplateResourcePath => "SRMS.Desktop.Resources.SixTest15ReportCardTemplate.xlsx";
        protected override string TemplateName => "SixTest15ReportCardTemplate.xlsx";
        protected override int SubjectStartRow => SubjectStartRow15;
        protected override int QrCodeRow => 35;

        protected override void PopulateSubjectScores(IWorksheet ws, ReportCardData d, List<Subject> activeSubjects, int startRow)
        {
            var specialCase = _specialCaseService.GetSpecialCaseConfig(d.StudentId, d.AcademicYear);
            bool hasSpecialCase = specialCase != null && specialCase.IsActive;
            var selectedTests = hasSpecialCase ? specialCase.GetSelectedTestsList() : null;
            bool showT1 = !hasSpecialCase || selectedTests?.Contains("T1") == true;
            bool showT2 = !hasSpecialCase || selectedTests?.Contains("T2") == true;
            bool showT3 = !hasSpecialCase || selectedTests?.Contains("T3") == true;
            bool showT4 = !hasSpecialCase || selectedTests?.Contains("T4") == true;
            bool showT5 = !hasSpecialCase || selectedTests?.Contains("T5") == true;
            bool showT6 = !hasSpecialCase || selectedTests?.Contains("T6") == true;
            bool isSss1 = d.CurrentClass?.Trim().ToUpperInvariant().Contains("SSS I") == true;

            int row = startRow;
            foreach (var subject in activeSubjects.Take(15))
            {
                var raw = d.SubjectScores.FirstOrDefault(s => s.SubjectId == subject.Id);
                ws.Range[row, 1].Text = subject.SubjectName ?? "";
                if (raw == null)
                {
                    for (int column = 5; column <= 15; column++) ws.Range[row, column].Clear();
                    row++;
                    continue;
                }

                var score = raw;
                if (hasSpecialCase)
                {
                    var calculated = _specialCaseService.CalculateScores(raw, specialCase, true, isSss1);
                    score = new SubjectScoreData
                    {
                        SubjectId = raw.SubjectId, T1 = raw.T1, T2 = raw.T2, T3 = raw.T3, T4 = raw.T4, T5 = raw.T5, T6 = raw.T6,
                        Term1Mean = calculated.term1Mean, Term2Mean = calculated.term2Mean, Term3Mean = calculated.term3Mean,
                        AnnualMean = calculated.finalScore, FinalScore = calculated.finalScore, Grade = Grade(calculated.finalScore)
                    };
                }

                Put(ws, row, 5, score.T1, showT1, ScoreType.TestScore);
                Put(ws, row, 6, score.T2, showT2, ScoreType.TestScore);
                Put(ws, row, 7, score.Term1Mean, true, ScoreType.MeanScore);
                Put(ws, row, 8, score.T3, showT3, ScoreType.TestScore);
                Put(ws, row, 9, score.T4, showT4, ScoreType.TestScore);
                Put(ws, row, 10, score.Term2Mean, true, ScoreType.MeanScore);
                Put(ws, row, 11, score.T5, showT5, ScoreType.TestScore);
                Put(ws, row, 12, score.T6, showT6, ScoreType.TestScore);
                Put(ws, row, 13, score.Term3Mean, true, ScoreType.MeanScore);
                Put(ws, row, 14, score.AnnualMean, true, ScoreType.MeanScore);
                ws.Range[row, 15].Text = Grade(score.AnnualMean);
                row++;
            }

            int subjectsWritten = Math.Min(activeSubjects.Count, 15);
            if (subjectsWritten == 0) return;
            foreach (int column in new[] { 5, 6, 8, 9, 11, 12 })
            {
                string letter = ColumnLetter(column);
                ws.Range[TotalRow, column].Formula = $"=SUM({letter}{SubjectStartRow15}:{letter}{SubjectEndRow})";
                ws.Range[AverageRow, column].Formula = $"=IF({letter}{TotalRow}=0,\"\",ROUND({letter}{TotalRow}/{subjectsWritten},0))";
            }
        }

        protected override void PopulateBehaviourScores(IWorksheet ws, ReportCardData d)
        {
            var b = d.BehaviourScores;
            if (b == null) return;
            int[] values = { b.Term1ClassParticipationT1, b.Term1ClassParticipationT2, b.Term2ClassParticipationT1, b.Term2ClassParticipationT2, b.Term3ClassParticipationT1, b.Term3ClassParticipationT2,
                b.Term1HonestyT1, b.Term1HonestyT2, b.Term2HonestyT1, b.Term2HonestyT2, b.Term3HonestyT1, b.Term3HonestyT2,
                b.Term1EffortT1, b.Term1EffortT2, b.Term2EffortT1, b.Term2EffortT2, b.Term3EffortT1, b.Term3EffortT2,
                b.Term1SelfDisciplineT1, b.Term1SelfDisciplineT2, b.Term2SelfDisciplineT1, b.Term2SelfDisciplineT2, b.Term3SelfDisciplineT1, b.Term3SelfDisciplineT2,
                b.Term1PunctualityT1, b.Term1PunctualityT2, b.Term2PunctualityT1, b.Term2PunctualityT2, b.Term3PunctualityT1, b.Term3PunctualityT2,
                b.Term1SchoolInvolvementT1, b.Term1SchoolInvolvementT2, b.Term2SchoolInvolvementT1, b.Term2SchoolInvolvementT2, b.Term3SchoolInvolvementT1, b.Term3SchoolInvolvementT2,
                b.Term1NeatnessT1, b.Term1NeatnessT2, b.Term2NeatnessT1, b.Term2NeatnessT2, b.Term3NeatnessT1, b.Term3NeatnessT2 };
            WriteSixColumnRows(ws, 13, values);
        }

        protected override void PopulatePsychomotorScores(IWorksheet ws, ReportCardData d)
        {
            var p = d.PsychomotorScores;
            if (p == null) return;
            int[] values = { p.Term1IndividualSportsT1, p.Term1IndividualSportsT2, p.Term2IndividualSportsT1, p.Term2IndividualSportsT2, p.Term3IndividualSportsT1, p.Term3IndividualSportsT2,
                p.Term1TeamPlayerT1, p.Term1TeamPlayerT2, p.Term2TeamPlayerT1, p.Term2TeamPlayerT2, p.Term3TeamPlayerT1, p.Term3TeamPlayerT2,
                p.Term1AthleticsT1, p.Term1AthleticsT2, p.Term2AthleticsT1, p.Term2AthleticsT2, p.Term3AthleticsT1, p.Term3AthleticsT2,
                p.Term1DrawingT1, p.Term1DrawingT2, p.Term2DrawingT1, p.Term2DrawingT2, p.Term3DrawingT1, p.Term3DrawingT2,
                p.Term1HandwritingT1, p.Term1HandwritingT2, p.Term2HandwritingT1, p.Term2HandwritingT2, p.Term3HandwritingT1, p.Term3HandwritingT2 };
            WriteSixColumnRows(ws, 26, values);
        }

        protected override void PopulateAttendance(IWorksheet ws, ReportCardData d, int studentId, string academicYear)
        {
            if (d.Attendance == null) return;
            var term1 = _dbHelper.GetAttendance(studentId, 1, academicYear);
            var term2 = _dbHelper.GetAttendance(studentId, 2, academicYear);
            if (term1?.Id > 0) { ws.Range[28, 4].Number = term1.DaysPresent; ws.Range[29, 4].Number = term1.DaysAbsent; }
            if (term2?.Id > 0) { ws.Range[28, 6].Number = term2.DaysPresent; ws.Range[29, 6].Number = term2.DaysAbsent; }
            ws.Range[28, 8].Number = d.Attendance.DaysPresent;
            ws.Range[29, 8].Number = d.Attendance.DaysAbsent;
        }

        protected override void PopulatePromotionInfo(IWorksheet ws, ReportCardData d, string teacherName, string displayClass)
        {
            ws.Range[34, 4].Text = GetClassTeacherComment(d.AnnualAverage);
            ws.Range[32, 5].Text = teacherName;
            bool promoted = d.PromotionResult?.IsPromoted ?? d.AnnualAverage >= 50;
            string nextClass = promoted ? ClassHelper.GetNextClass(d.CurrentClass ?? "") : displayClass;
            ws.Range[37, 16].Text = promoted ? $"PROMOTED TO {nextClass}" : $"REPEAT {displayClass}";
            ws.Range[34, 16].Text = GetPrincipalComment(promoted, d.AnnualAverage);
            ws.Range[36, 16].Text = d.PrincipalName ?? "Principal";
        }

        protected override void PopulateSubjectAnalysis(IWorksheet ws, ReportCardData d)
        {
            ws.Range[38, 5].Number = d.CoreSubjectsPassed;
            ws.Range[38, 12].Number = d.OptionalSubjectsPassed;
            ws.Range[39, 5].Number = d.TotalSubjectsPassed;
            ws.Range[39, 13].Number = Convert.ToDouble(d.AnnualAverage);
        }

        private static void Put(IWorksheet ws, int row, int column, decimal? value, bool show, ScoreType type)
        {
            if (show && value.HasValue) ScoreColorCoding.ApplyToExcelCell(ws.Range[row, column], value.Value, type);
            else ws.Range[row, column].Clear();
        }

        private static void WriteSixColumnRows(IWorksheet ws, int startRow, int[] values)
        {
            for (int index = 0; index < values.Length / 6; index++)
            {
                int row = startRow + index;
                ws.Range[row, 21].Number = values[index * 6]; ws.Range[row, 22].Number = values[index * 6 + 1];
                ws.Range[row, 24].Number = values[index * 6 + 2]; ws.Range[row, 25].Number = values[index * 6 + 3];
                ws.Range[row, 27].Number = values[index * 6 + 4]; ws.Range[row, 28].Number = values[index * 6 + 5];
            }
        }

        private static string Grade(decimal score)
        {
            if (score >= 80) return "A";
            if (score >= 70) return "B";
            if (score >= 60) return "C";
            if (score >= 50) return "D";
            if (score >= 40) return "E";
            return "F";
        }

        private static string ColumnLetter(int column)
        {
            string result = "";
            while (column > 0) { column--; result = (char)('A' + column % 26) + result; column /= 26; }
            return result;
        }
    }
}
