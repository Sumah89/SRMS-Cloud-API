#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SRMS.Cloud.Data;
using SRMS.Desktop.Models.Reports;

namespace SRMS.Cloud
{
    public static class ReportEndpoints
    {
        public static void Map(WebApplication app)
        {
            app.MapGet("/api/reports/top-performers",
                async (HttpContext ctx, PostgresDbContext db,
                       int? classId, int term, string year, int? topN) =>
                {
                    if (!TryGetSchoolId(ctx, out int schoolId)) return Results.Unauthorized();

                    var list = await BuildTopPerformersAsync(
                        db, schoolId, classId, term, year ?? "", topN ?? 10);

                    return Results.Ok(new TopPerformerListResponse
                    {
                        Performers = list,
                        Count = list.Count
                    });
                }).RequireAuthorization();

            app.MapGet("/api/bece/candidates",
                async (HttpContext ctx, PostgresDbContext db, string year) =>
                {
                    if (!TryGetSchoolId(ctx, out int schoolId)) return Results.Unauthorized();

                    var list = new List<BECECandidateDto>();
                    try
                    {
                        var rows = await db.Database
                            .SqlQueryRaw<BECECandidateRaw>(
                                @"SELECT ""StudentId"", ""IndexNumber""
                                  FROM ""BECECandidateRecords""
                                  WHERE ""SchoolId"" = {0}
                                    AND TRIM(IFNULL(""AcademicYear"", '')) = {1}
                                    AND (""IsRemoved"" = FALSE OR ""IsRemoved"" IS NULL)",
                                schoolId, year ?? "")
                            .ToListAsync();

                        var studentIds = rows.Select(r => r.StudentId).Distinct().ToList();
                        var students = await db.Students
                            .Where(s => studentIds.Contains(s.Id))
                            .ToDictionaryAsync(s => s.Id);

                        foreach (var r in rows)
                        {
                            students.TryGetValue(r.StudentId, out var s);
                            list.Add(new BECECandidateDto
                            {
                                StudentId    = r.StudentId,
                                FullName     = s?.FullName ?? "—",
                                IndexNumber  = r.IndexNumber ?? "—",
                                Gender       = s?.Gender ?? "—"
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ReportEndpoints] BECE candidates: {ex.Message}");
                    }

                    return Results.Ok(new BECECandidateListResponse { Candidates = list });
                }).RequireAuthorization();

            app.MapPost("/api/reports/batch",
                (HttpContext ctx, PostgresDbContext db, BatchReportRequest req) =>
                {
                    if (!TryGetSchoolId(ctx, out _)) return Results.Unauthorized();
                    return Results.Ok(new BatchReportResult
                    {
                        Url = null,
                        Count = req?.Count ?? 0
                    });
                }).RequireAuthorization();
        }

        private static async Task<List<TopPerformerDto>> BuildTopPerformersAsync(
            PostgresDbContext db, int schoolId, int? classId, int term, string year, int topN)
        {
            if (topN <= 0) topN = 10;
            if (topN > 500) topN = 500;
            if (string.IsNullOrWhiteSpace(year)) return new List<TopPerformerDto>();

            var classQuery = db.Classes.Where(c => c.SchoolId == schoolId);
            if (classId.HasValue) classQuery = classQuery.Where(c => c.Id == classId.Value);
            var classes = await classQuery.ToListAsync();
            if (classes.Count == 0) return new List<TopPerformerDto>();

            var classIds   = classes.Select(c => c.Id).ToList();
            var classNames = classes.ToDictionary(c => c.Id, c => c.ClassName);

            var students = await db.Students
                .Where(s => s.SchoolId == schoolId && classIds.Contains(s.ClassId))
                .ToListAsync();
            if (students.Count == 0) return new List<TopPerformerDto>();

            var studentIds = students.Select(s => s.Id).ToList();
            var scores = await db.AcademicPerformances
                .Where(a => studentIds.Contains(a.StudentId)
                         && a.Term == term
                         && a.AcademicYear == year)
                .ToListAsync();

            var byStudent = scores.GroupBy(a => a.StudentId);

            var results = new List<TopPerformerDto>();
            foreach (var grp in byStudent)
            {
                var student = students.FirstOrDefault(s => s.Id == grp.Key);
                if (student == null) continue;

                var scoreList = grp.ToList();
                decimal avg = scoreList.Count > 0
                    ? scoreList.Average(x => x.FinalScore ?? 0m)   // ← fix: nullable
                    : 0m;

                results.Add(new TopPerformerDto
                {
                    StudentId       = student.Id,
                    FullName        = student.FullName,
                    AdmissionNumber = student.AdmissionNumber,
                    ClassId         = student.ClassId,
                    ClassName       = classNames.TryGetValue(student.ClassId, out var cn) ? cn : null,
                    AnnualAverage   = Math.Round(avg, 2),
                    Grade           = GradeFor(avg)
                });
            }

            return results
                .OrderByDescending(x => x.AnnualAverage)
                .Take(topN)
                .Select((x, i) => { x.Rank = i + 1; return x; })
                .ToList();
        }

        private static string GradeFor(decimal avg) => avg switch
        {
            >= 80 => "A",
            >= 70 => "B",
            >= 60 => "C",
            >= 50 => "D",
            >= 40 => "E",
            _     => "F"
        };

        private static bool TryGetSchoolId(HttpContext ctx, out int schoolId)
        {
            schoolId = 0;
            return int.TryParse(ctx.User.FindFirst("school_id")?.Value, out schoolId)
                && schoolId > 0;
        }

        private sealed class BECECandidateRaw
        {
            public int StudentId { get; set; }
            public string IndexNumber { get; set; }
        }
    }
}