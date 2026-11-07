using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using SRMS.Desktop.Data;
using SRMS.Desktop.Models;
using SRMS.Desktop.Helpers;

namespace SRMS.Desktop.Services
{
    public class ReportDataService : IReportDataService, IDisposable
    {
        // ── Default pass marks used when criteria doesn't specify ──
        private const decimal DefaultCorePassMark = 50m;
        private const decimal DefaultOptionalPassMark = 50m;
        private const decimal DefaultTotalPassMark = 50m;
        private const decimal DefaultEnglishMathsPassMark = 50m;

        private readonly DatabaseHelper _dbHelper;
        private readonly ILogger _logger;
        private bool _disposed = false;

        public ReportDataService()
        {
            _dbHelper = new DatabaseHelper();
            _logger = Logger.Instance;
        }

        // ══════════════════════════════════════════════════════════
        //  Change notification
        // ══════════════════════════════════════════════════════════
        public class ScoresChangedEventArgs
        {
            public int StudentId { get; set; }
            public int? ClassId { get; set; }
            public string AcademicYear { get; set; }
        }

        public static event Action ScoresChanged;
        public static event Action<ScoresChangedEventArgs> ScoresChangedEx;

        public static void NotifyScoresChanged()
        {
            try { ScoresChanged?.Invoke(); } catch { }
        }

        public static void NotifyScoresChanged(int studentId, int? classId, string academicYear)
        {
            try { ScoresChanged?.Invoke(); } catch { }
            try
            {
                ScoresChangedEx?.Invoke(new ScoresChangedEventArgs
                {
                    StudentId = studentId,
                    ClassId = classId,
                    AcademicYear = academicYear
                });
            }
            catch { }
        }

        // ══════════════════════════════════════════════════════════
        //  Single-student API (signature unchanged)
        // ══════════════════════════════════════════════════════════
        public async Task<ReportCardData> GetReportCardDataAsync(int studentId, int term, string academicYear)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var student = _dbHelper.GetStudentById(studentId);
                    if (student == null)
                    {
                        _logger.LogWarning($"Student not found: {studentId}");
                        return null;
                    }

                    var classObj = _dbHelper.GetClassById(student.ClassId);
                    var school = _dbHelper.GetSchoolById(student.SchoolId);
                    var subjects = _dbHelper.GetSubjects(student.SchoolId, student.ClassId);
                    var criteria = PromotionCriteriaService.GetCriteria(student.SchoolId, student.ClassId);

                    var term1Scores = GetRawSubjectScores(studentId, 1, academicYear);
                    var term2Scores = GetRawSubjectScores(studentId, 2, academicYear);
                    var term3Scores = GetRawSubjectScores(studentId, 3, academicYear);

                    var rawScoresByTerm = new Dictionary<int, List<SubjectScoreData>>
                    {
                        { 1, term1Scores },
                        { 2, term2Scores },
                        { 3, term3Scores }
                    };

                    var behaviour = SafeGet(() =>
                        _dbHelper.GetBehaviourScores(studentId, term, academicYear)) ?? new BehaviourScoreData();

                    var psychomotor = SafeGet(() =>
                        _dbHelper.GetPsychomotorScores(studentId, term, academicYear)) ?? new PsychomotorScoreData();

                    var attendance = SafeGet(() =>
                        _dbHelper.GetAttendance(studentId, term, academicYear));

                    return BuildReportCardData(
                        student, classObj, school, subjects, criteria,
                        term, academicYear, rawScoresByTerm,
                        behaviour, psychomotor, attendance);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error getting report card data for student {studentId}");
                    return null;
                }
            });
        }

        // ══════════════════════════════════════════════════════════
        //  Bulk API — one class, one call, minimal DB round-trips
        // ══════════════════════════════════════════════════════════
        /// <summary>
        /// Loads report card data for every active student in a class in one batch.
        /// Loads shared lookups (class, school, subjects, criteria) once; all raw
        /// scores for the class in three queries (one per term); behaviour,
        /// psychomotor and attendance in three more. CPU-bound scoring then runs
        /// in parallel across students.
        /// </summary>
        public async Task<List<ReportCardData>> GetBulkReportCardDataAsync(
            int classId, int term, string academicYear)
        {
            return await Task.Run(() =>
            {
                var results = new List<ReportCardData>();
                try
                {
                    var classObj = _dbHelper.GetClassById(classId);
                    if (classObj == null)
                    {
                        _logger.LogWarning($"Class not found: {classId}");
                        return results;
                    }

                    int schoolId = classObj.SchoolId;
                    var students = _dbHelper.GetStudents(schoolId, classId) ?? new List<Student>();
                    if (students.Count == 0) return results;

                    var school = _dbHelper.GetSchoolById(schoolId);
                    var subjects = _dbHelper.GetSubjects(schoolId, classId);
                    var criteria = PromotionCriteriaService.GetCriteria(schoolId, classId);

                    // ── Bulk raw scores: 3 queries total ──
                    var scoresByTerm = new Dictionary<int, Dictionary<int, List<SubjectScoreData>>>
                    {
                        { 1, LoadAllRawScoresForClass(classId, 1, academicYear) },
                        { 2, LoadAllRawScoresForClass(classId, 2, academicYear) },
                        { 3, LoadAllRawScoresForClass(classId, 3, academicYear) }
                    };

                    // ── Bulk behaviour / psychomotor / attendance: 3 queries total ──
                    var behaviourMap = SafeGetDict(() =>
                        _dbHelper.GetBehaviourScoresForClass(classId, term, academicYear));

                    var psychomotorMap = SafeGetDict(() =>
                        _dbHelper.GetPsychomotorScoresForClass(classId, term, academicYear));

                    var attendanceMap = SafeGetDict(() =>
                        _dbHelper.GetAttendanceForClass(classId, term, academicYear));

                    // ── Build cards in parallel (CPU-bound) ──
                    var bag = new ConcurrentBag<ReportCardData>();

                    Parallel.ForEach(
                        students,
                        new ParallelOptions
                        {
                            MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount - 1)
                        },
                        student =>
                        {
                            try
                            {
                                var rawScoresByTerm = new Dictionary<int, List<SubjectScoreData>>
                                {
                                    { 1, GetScoresForStudent(scoresByTerm[1], student.Id) },
                                    { 2, GetScoresForStudent(scoresByTerm[2], student.Id) },
                                    { 3, GetScoresForStudent(scoresByTerm[3], student.Id) }
                                };

                                behaviourMap.TryGetValue(student.Id, out var behaviour);
                                psychomotorMap.TryGetValue(student.Id, out var psychomotor);
                                attendanceMap.TryGetValue(student.Id, out var attendance);

                                var card = BuildReportCardData(
                                    student, classObj, school, subjects, criteria,
                                    term, academicYear, rawScoresByTerm,
                                    behaviour ?? new BehaviourScoreData(),
                                    psychomotor ?? new PsychomotorScoreData(),
                                    attendance);

                                if (card != null)
                                    bag.Add(card);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex,
                                    $"Error building bulk report card for student {student.Id}");
                            }
                        });

                    // Preserve original student ordering for stable output.
                    var lookup = bag.ToDictionary(c => c.StudentId);
                    foreach (var s in students)
                    {
                        if (lookup.TryGetValue(s.Id, out var card))
                            results.Add(card);
                    }

                    return results;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        $"Error in GetBulkReportCardDataAsync(classId={classId}, term={term})");
                    return results;
                }
            });
        }

        // ══════════════════════════════════════════════════════════
        //  Core builder — shared by single & bulk paths
        // ══════════════════════════════════════════════════════════
        private ReportCardData BuildReportCardData(
            Student student,
            Class classObj,
            School school,
            List<Subject> allSubjects,
            SchoolPromotionCriteria activeCriteria,
            int term,
            string academicYear,
            Dictionary<int, List<SubjectScoreData>> rawScoresByTerm,
            BehaviourScoreData behaviour,
            PsychomotorScoreData psychomotor,
            AttendanceData attendance)
        {
            var data = new ReportCardData
            {
                StudentId = student.Id,
                StudentName = student.FullName ?? "",
                AdmissionNumber = student.AdmissionNumber ?? "",
                DateOfBirth = student.DateOfBirth,
                Gender = student.Gender ?? "",
                ClassId = student.ClassId,
                AcademicYear = academicYear ?? GetCurrentAcademicYear()
            };

            // ── Photo path ──
            if (!string.IsNullOrEmpty(student.PhotoPath))
            {
                data.StudentPhotoPath = student.PhotoPath;
            }
            else
            {
                try
                {
                    var photosDir = Global.AppDataPath != null
                        ? System.IO.Path.Combine(Global.AppDataPath, "StudentPhotos")
                        : null;

                    if (!string.IsNullOrEmpty(photosDir))
                    {
                        var candidate = System.IO.Path.Combine(photosDir, $"student_{student.Id}.jpg");
                        if (System.IO.File.Exists(candidate))
                            data.StudentPhotoPath = candidate;
                    }
                }
                catch { }
            }

            if (classObj != null)
                data.CurrentClass = classObj.ClassName ?? "";

            if (school != null)
            {
                data.SchoolId = school.Id;
                data.SchoolName = school.SchoolName ?? "";
                data.PrincipalName = school.PrincipalName ?? "";
                data.SchoolDistrict = school.District ?? "";
                data.SchoolTown = school.Town ?? "";
            }

            // ── Result type via resolver ──
            string resultType = ResultTypeResolver.Resolve(student.SchoolId, student.ClassId);

            var activeSubjects = allSubjects?
                .Where(s => s.IsActive)
                .OrderBy(s => s.SubjectCode)
                .ToList() ?? new List<Subject>();

            // ── Pass marks from criteria ──
            var pass = LoadPassMarks(activeCriteria);
            decimal corePassMark = pass.Core;
            decimal optionalPassMark = pass.Optional;
            decimal totalPassMark = pass.Total;
            decimal englishMathsPassMark = pass.EnglishMaths;

            bool englishMathsPassed = false;

            data.SubjectScores = new List<SubjectScoreData>();

            decimal term1Total = 0, term2Total = 0, term3Total = 0;
            int term1Count = 0, term2Count = 0, term3Count = 0;
            decimal totalFinalScore = 0;
            int scoredSubjectsCount = 0;
            int corePassedCount = 0;
            int optionalPassedCount = 0;
            int totalPassedCount = 0;

            bool isSSS1 = data.CurrentClass != null &&
                          data.CurrentClass.Trim().ToUpperInvariant() == "SSS I";

            var specialCaseConfig = _dbHelper.GetStudentSpecialCase(student.Id, academicYear);
            bool isSpecialCase = specialCaseConfig != null && specialCaseConfig.IsActive;
            var specialCaseService = new SpecialCaseService();

            var term1Scores = rawScoresByTerm.TryGetValue(1, out var t1List)
                ? t1List : new List<SubjectScoreData>();
            var term2Scores = rawScoresByTerm.TryGetValue(2, out var t2List)
                ? t2List : new List<SubjectScoreData>();
            var term3Scores = rawScoresByTerm.TryGetValue(3, out var t3List)
                ? t3List : new List<SubjectScoreData>();

            foreach (var subject in activeSubjects)
            {
                var term1Score = term1Scores.FirstOrDefault(s => s.SubjectId == subject.Id);
                var term2Score = term2Scores.FirstOrDefault(s => s.SubjectId == subject.Id);
                var term3Score = term3Scores.FirstOrDefault(s => s.SubjectId == subject.Id);

                decimal t1 = (term1Score?.T1).GetValueOrDefault(0);
                decimal t2 = (term1Score?.T2).GetValueOrDefault(0);
                decimal t3 = (term2Score?.T3).GetValueOrDefault(0);
                decimal t4 = (term2Score?.T4).GetValueOrDefault(0);
                decimal t5 = (term3Score?.T5).GetValueOrDefault(0);
                decimal t6 = (term3Score?.T6).GetValueOrDefault(0);
                decimal exam = (term3Score?.Exam).GetValueOrDefault(0);

                var subjectData = new SubjectScoreData
                {
                    SubjectId = subject.Id,
                    SubjectName = subject.SubjectName ?? "",
                    IsCore = subject.IsCore,
                    T1 = t1,
                    T2 = t2,
                    T3 = t3,
                    T4 = t4,
                    T5 = t5,
                    T6 = t6,
                    Exam = exam,
                    StudentId = student.Id,
                    Term = term,
                    AcademicYear = academicYear ?? ""
                };

                bool hasAnyScore = t1 > 0 || t2 > 0 || t3 > 0 || t4 > 0 ||
                                   t5 > 0 || t6 > 0 || exam > 0;

                if (isSpecialCase)
                {
                    var calculated = specialCaseService.CalculateScores(
                        subjectData, specialCaseConfig,
                        resultType == "SixTests", isSSS1);

                    subjectData.Term1Mean = calculated.term1Mean;
                    subjectData.Term2Mean = calculated.term2Mean;
                    subjectData.Term3Mean = calculated.term3Mean;
                    subjectData.Percent60 = calculated.percent60;
                    subjectData.FinalScore = calculated.finalScore;
                    subjectData.Grade = GetGrade(calculated.finalScore);
                    subjectData.IsPassed = hasAnyScore && calculated.finalScore >= totalPassMark;
                }
                else if (resultType == "SixTests")
                {
                    if (isSSS1)
                    {
                        subjectData.Term1Mean = t2;
                        subjectData.Term2Mean = (t3 + t4) / 2;
                        subjectData.Term3Mean = (t5 + t6) / 2;
                        subjectData.FinalScore = (t2 + t3 + t4 + t5 + t6) /
                            (decimal)((t2 == 0 && t3 == 0 && t4 == 0 && t5 == 0 && t6 == 0) ? 1 : 5);
                        subjectData.Grade = hasAnyScore ? GetGrade(subjectData.FinalScore) : "";
                        subjectData.IsPassed = hasAnyScore && subjectData.FinalScore >= totalPassMark;
                    }
                    else
                    {
                        subjectData.Term1Mean = (t1 + t2) / 2;
                        subjectData.Term2Mean = (t3 + t4) / 2;
                        subjectData.Term3Mean = (t5 + t6) / 2;
                        subjectData.FinalScore = (t1 + t2 + t3 + t4 + t5 + t6) / 6;
                        subjectData.Grade = hasAnyScore ? GetGrade(subjectData.FinalScore) : "";
                        subjectData.IsPassed = hasAnyScore && subjectData.FinalScore >= totalPassMark;
                    }
                }
                else // Five Tests
                {
                    if (isSSS1)
                    {
                        subjectData.Term1Mean = t2;
                        subjectData.Term2Mean = (t3 + t4) / 2;
                        subjectData.Percent60 = (t2 + t3 + t4) * 0.2m;
                        subjectData.Percent40 = exam;
                        subjectData.FinalScore = subjectData.Percent60 + subjectData.Percent40;
                        subjectData.Grade = hasAnyScore ? GetGrade(subjectData.FinalScore) : "";
                        subjectData.IsPassed = hasAnyScore && subjectData.FinalScore >= totalPassMark;
                    }
                    else
                    {
                        subjectData.Term1Mean = (t1 + t2) / 2;
                        subjectData.Term2Mean = (t3 + t4) / 2;
                        decimal testsTotal = t1 + t2 + t3 + t4;
                        subjectData.Percent60 = testsTotal * 0.15m;
                        subjectData.Percent40 = exam;
                        subjectData.FinalScore = subjectData.Percent60 + subjectData.Percent40;
                        subjectData.Grade = hasAnyScore ? GetGrade(subjectData.FinalScore) : "";
                        subjectData.IsPassed = hasAnyScore && subjectData.FinalScore >= totalPassMark;
                    }
                }

                data.SubjectScores.Add(subjectData);

                // ── Pass counting ──
                if (hasAnyScore)
                {
                    if (subject.IsCore)
                    {
                        if (subjectData.FinalScore >= corePassMark) corePassedCount++;
                    }
                    else
                    {
                        if (subjectData.FinalScore >= optionalPassMark) optionalPassedCount++;
                    }

                    if (subjectData.FinalScore >= totalPassMark) totalPassedCount++;

                    if (!englishMathsPassed && subjectData.FinalScore >= englishMathsPassMark)
                    {
                        string name = (subject.SubjectName ?? "").ToLowerInvariant();
                        if (name.Contains("english") || name.Contains("math"))
                            englishMathsPassed = true;
                    }

                    totalFinalScore += subjectData.FinalScore;
                    scoredSubjectsCount++;

                    term1Total += subjectData.Term1Mean;
                    term2Total += subjectData.Term2Mean;
                    term3Total += subjectData.FinalScore;
                    term1Count++;
                    term2Count++;
                    term3Count++;
                }
            }

            data.CoreSubjectsPassed = corePassedCount;
            data.OptionalSubjectsPassed = optionalPassedCount;
            data.TotalSubjectsPassed = totalPassedCount;
            data.TotalSubjectsCount = activeSubjects.Count;

            data.EnglishMathsRequired = activeCriteria != null && activeCriteria.RequireEnglishOrMaths;
            data.EnglishMathsPassed = englishMathsPassed;

            data.Term1Average = term1Count > 0 ? term1Total / term1Count : 0;
            data.Term2Average = term2Count > 0 ? term2Total / term2Count : 0;
            data.Term3Average = term3Count > 0 ? term3Total / term3Count : 0;
            data.AnnualAverage = scoredSubjectsCount > 0 ? totalFinalScore / scoredSubjectsCount : 0;

            var promotionEngine = new PromotionEngine();
            data.PromotionResult = promotionEngine.Evaluate(data, activeCriteria);

            data.BehaviourScores = behaviour ?? new BehaviourScoreData();
            data.PsychomotorScores = psychomotor ?? new PsychomotorScoreData();
            data.Attendance = attendance;

            return data;
        }

        // ══════════════════════════════════════════════════════════
        //  Bulk-load helpers
        // ══════════════════════════════════════════════════════════

        private Dictionary<int, List<SubjectScoreData>> LoadAllRawScoresForClass(
            int classId, int term, string academicYear)
        {
            var map = new Dictionary<int, List<SubjectScoreData>>();
            try
            {
                const string sql = @"
                    SELECT ap.*
                    FROM AcademicPerformance ap
                    INNER JOIN Students s ON s.Id = ap.StudentId
                    WHERE s.ClassId = @ClassId
                      AND (s.IsDeleted = 0 OR s.IsDeleted IS NULL)
                      AND ap.Term = @Term
                      AND ap.AcademicYear = @AcademicYear";

                var parameters = new Dictionary<string, object>
                {
                    { "@ClassId", classId },
                    { "@Term", term },
                    { "@AcademicYear", academicYear ?? "" }
                };

                DataTable dt = _dbHelper.ExecuteQuery(sql, parameters);
                if (dt == null || dt.Rows.Count == 0) return map;

                foreach (DataRow row in dt.Rows)
                {
                    int studentId = Convert.ToInt32(row["StudentId"]);
                    var score = new SubjectScoreData
                    {
                        StudentId = studentId,
                        SubjectId = Convert.ToInt32(row["SubjectId"]),
                        T1 = row["T1"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["T1"]),
                        T2 = row["T2"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["T2"]),
                        T3 = row["T3"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["T3"]),
                        T4 = row["T4"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["T4"]),
                        T5 = row["T5"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["T5"]),
                        T6 = row["T6"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["T6"]),
                        Exam = row["Exam"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["Exam"])
                    };

                    if (!map.TryGetValue(studentId, out var list))
                    {
                        list = new List<SubjectScoreData>();
                        map[studentId] = list;
                    }
                    list.Add(score);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error bulk-loading scores for class {classId}, term {term}");
            }
            return map;
        }

        private static List<SubjectScoreData> GetScoresForStudent(
            Dictionary<int, List<SubjectScoreData>> map, int studentId)
        {
            if (map != null && map.TryGetValue(studentId, out var list))
                return list;
            return new List<SubjectScoreData>();
        }

        private Dictionary<int, T> SafeGetDict<T>(Func<Dictionary<int, T>> fn)
        {
            try
            {
                return fn() ?? new Dictionary<int, T>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bulk lookup failed");
                return new Dictionary<int, T>();
            }
        }

        // ══════════════════════════════════════════════════════════
        //  Pass-mark helper
        // ══════════════════════════════════════════════════════════
        private struct PassMarks
        {
            public decimal Core;
            public decimal Optional;
            public decimal Total;
            public decimal EnglishMaths;
        }

        private static PassMarks LoadPassMarks(SchoolPromotionCriteria criteria)
        {
            return new PassMarks
            {
                Core = (criteria != null && criteria.CoreSubjectPassMark > 0)
                    ? criteria.CoreSubjectPassMark : DefaultCorePassMark,
                Optional = (criteria != null && criteria.OptionalSubjectPassMark > 0)
                    ? criteria.OptionalSubjectPassMark : DefaultOptionalPassMark,
                Total = (criteria != null && criteria.TotalPassMark > 0)
                    ? criteria.TotalPassMark : DefaultTotalPassMark,
                EnglishMaths = (criteria != null && criteria.EnglishMathsPassMark > 0)
                    ? criteria.EnglishMathsPassMark : DefaultEnglishMathsPassMark
            };
        }

        private static T SafeGet<T>(Func<T> fn)
        {
            try { return fn(); } catch { return default(T); }
        }

        // ══════════════════════════════════════════════════════════
        //  Raw scores (single-student)
        // ══════════════════════════════════════════════════════════
        private List<SubjectScoreData> GetRawSubjectScores(int studentId, int term, string academicYear)
        {
            var results = new List<SubjectScoreData>();
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "@StudentId", studentId },
                    { "@Term", term },
                    { "@AcademicYear", academicYear ?? "" }
                };

                DataTable dt = _dbHelper.ExecuteQuery(
                    "SELECT * FROM AcademicPerformance WHERE StudentId = @StudentId AND Term = @Term AND AcademicYear = @AcademicYear",
                    parameters);

                if (dt == null || dt.Rows.Count == 0) return results;

                foreach (DataRow row in dt.Rows)
                {
                    results.Add(new SubjectScoreData
                    {
                        StudentId = studentId,
                        SubjectId = Convert.ToInt32(row["SubjectId"]),
                        T1 = row["T1"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["T1"]),
                        T2 = row["T2"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["T2"]),
                        T3 = row["T3"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["T3"]),
                        T4 = row["T4"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["T4"]),
                        T5 = row["T5"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["T5"]),
                        T6 = row["T6"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["T6"]),
                        Exam = row["Exam"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["Exam"])
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting raw scores for student {studentId}, term {term}");
            }
            return results;
        }

        // ══════════════════════════════════════════════════════════
        //  Grade + academic year
        // ══════════════════════════════════════════════════════════
        private string GetGrade(decimal score)
        {
            if (score >= 80) return "A";
            if (score >= 70) return "B";
            if (score >= 60) return "C";
            if (score >= 50) return "D";
            if (score >= 40) return "E";
            return "F";
        }

        private string GetCurrentAcademicYear()
        {
            int year = DateTime.Now.Year;
            if (DateTime.Now.Month <= 8)
                return $"{year - 1}/{year}";
            return $"{year}/{year + 1}";
        }

        // ══════════════════════════════════════════════════════════
        //  Interface implementations
        // ══════════════════════════════════════════════════════════
        public async Task<List<SubjectScoreData>> GetSubjectScoresAsync(int studentId, int term, string academicYear)
        {
            return await Task.Run(() => GetRawSubjectScores(studentId, term, academicYear));
        }

        public async Task<BehaviourScoreData> GetBehaviourScoresAsync(int studentId, int term, string academicYear)
        {
            return await Task.Run(() => _dbHelper.GetBehaviourScores(studentId, term, academicYear));
        }

        public async Task<PsychomotorScoreData> GetPsychomotorScoresAsync(int studentId, int term, string academicYear)
        {
            return await Task.Run(() => _dbHelper.GetPsychomotorScores(studentId, term, academicYear));
        }

        public async Task<AttendanceData> GetAttendanceAsync(int studentId, int term, string academicYear)
        {
            return await Task.Run(() => _dbHelper.GetAttendance(studentId, term, academicYear));
        }

        public async Task<SchoolReportConfig> GetSchoolConfigAsync(int schoolId)
        {
            return await Task.Run(() =>
                _dbHelper.GetSchoolReportConfig(schoolId)
                ?? new SchoolReportConfig { SchoolId = schoolId });
        }

        public async Task<SchoolPromotionCriteria> GetPromotionCriteriaAsync(int schoolId, int? classId = null)
        {
            return await Task.Run(() => PromotionCriteriaService.GetCriteria(schoolId, classId));
        }

        public async Task<List<ReportSubjectRowMapping>> GetSubjectRowMappingsAsync(int schoolId, int? classId = null)
        {
            return await Task.Run(() => new List<ReportSubjectRowMapping>());
        }

        public async Task<bool> SavePromotionCriteriaAsync(SchoolPromotionCriteria criteria)
        {
            if (criteria == null) return false;
            return await Task.Run(() => PromotionCriteriaService.SaveCriteria(criteria));
        }

        public async Task<bool> SaveSchoolConfigAsync(SchoolReportConfig config)
        {
            if (config == null) return false;
            return await Task.Run(() => _dbHelper.SaveSchoolReportConfig(config));
        }

        public async Task<PromotionResult> GetStudentPromotionStatusAsync(int studentId, string academicYear)
        {
            var reportData = await GetReportCardDataAsync(studentId, 3, academicYear);
            if (reportData == null) return null;

            var promotionEngine = new PromotionEngine();
            var criteria = await GetPromotionCriteriaAsync(reportData.SchoolId, reportData.ClassId);
            return promotionEngine.Evaluate(reportData, criteria);
        }

        public async Task<ClassPromotionSummary> GetClassPromotionSummaryAsync(int classId, string academicYear)
        {
            var classObj = _dbHelper.GetClassById(classId);
            if (classObj == null) return null;

            var students = _dbHelper.GetStudents(classObj.SchoolId, classId);
            var summary = new ClassPromotionSummary
            {
                ClassName = classObj.ClassName,
                TotalStudents = students.Count,
                AcademicYear = academicYear
            };

            var criteria = await GetPromotionCriteriaAsync(classObj.SchoolId, classId);
            var promotionEngine = new PromotionEngine();
            decimal totalAverageSum = 0;

            // Prefer the bulk path so this doesn't N+1 the DB.
            var cards = await GetBulkReportCardDataAsync(classId, 3, academicYear);
            var cardLookup = cards.ToDictionary(c => c.StudentId);

            foreach (var student in students)
            {
                if (!cardLookup.TryGetValue(student.Id, out var reportData) || reportData == null)
                    continue;

                var result = promotionEngine.Evaluate(reportData, criteria);
                if (result.IsPromoted) summary.PromotedCount++;
                else summary.RepeatCount++;

                totalAverageSum += reportData.AnnualAverage;
            }

            summary.AverageScore = summary.TotalStudents > 0
                ? totalAverageSum / summary.TotalStudents : 0;
            summary.TotalAverage = totalAverageSum;
            summary.PromotionRate = summary.TotalStudents > 0
                ? (double)summary.PromotedCount / summary.TotalStudents * 100 : 0;

            return summary;
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

    /// <summary>
    /// Class promotion summary data
    /// </summary>
    public class ClassPromotionSummary
    {
        public string ClassName { get; set; }
        public int TotalStudents { get; set; }
        public int PromotedCount { get; set; }
        public int RepeatCount { get; set; }
        public decimal AverageScore { get; set; }
        public double PromotionRate { get; set; }
        public string AcademicYear { get; set; }
        public decimal TotalAverage { get; set; }

        public string PromotionRateFormatted => $"{PromotionRate:F1}%";
        public string AverageScoreFormatted => $"{AverageScore:F1}%";
    }
}