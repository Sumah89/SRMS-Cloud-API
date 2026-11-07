#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SRMS.Cloud.Data;

namespace SRMS.Cloud
{
    public static class ParentEndpoints
    {
        public static void Map(WebApplication app)
        {
            // ─── /api/parent/children ────────────────────────────────
            app.MapGet("/api/parent/children", async (HttpContext ctx, PostgresDbContext db) =>
            {
                if (!IsRole(ctx, "parent")) return Results.Forbid();
                int[] childIds = GetChildIds(ctx);
                if (childIds.Length == 0) return Results.Forbid();

                var students = await db.Students
                    .Where(s => childIds.Contains(s.Id))
                    .ToListAsync();

                var classIds = students.Select(s => s.ClassId).Distinct().ToList();
                var classes  = await db.Classes
                    .Where(c => classIds.Contains(c.Id))
                    .ToDictionaryAsync(c => c.Id, c => c.ClassName);

                DateTime todayStart = DateTime.UtcNow.Date;
                DateTime todayEnd   = todayStart.AddDays(1);
                var todaysMarks = await db.DailyAttendances
                    .Where(a => childIds.Contains(a.StudentId)
                             && a.Date >= todayStart
                             && a.Date <  todayEnd)
                    .ToListAsync();

                var todayByStudent = todaysMarks
                    .GroupBy(a => a.StudentId)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).First().Status ?? "");

                var children = students.Select(s => new
                {
                    studentId       = s.Id,
                    fullName        = s.FullName,
                    admissionNumber = s.AdmissionNumber,
                    classId         = s.ClassId,
                    className       = classes.TryGetValue(s.ClassId, out var cn) ? cn : "",
                    schoolId        = s.SchoolId,
                    todayAttendance = todayByStudent.TryGetValue(s.Id, out var st) ? st : ""
                }).ToList();

                return Results.Ok(new { children });
            }).RequireAuthorization();

            // ─── /api/parent/children/{id}/attendance ────────────────
            app.MapGet("/api/parent/children/{studentId:int}/attendance", async (
                int studentId, int term, string year, HttpContext ctx, PostgresDbContext db) =>
            {
                if (!IsRole(ctx, "parent")) return Results.Forbid();
                if (!IsChildOfCaller(ctx, studentId)) return Results.Forbid();

                string academicYear = string.IsNullOrWhiteSpace(year)
                    ? DateTime.Now.Year.ToString()
                    : year;

                var att = await db.Attendances.FirstOrDefaultAsync(a =>
                    a.StudentId == studentId && a.Term == term && a.AcademicYear == academicYear);

                return Results.Ok(new
                {
                    studentId,
                    term          = att?.Term ?? term,
                    academicYear  = att?.AcademicYear ?? academicYear,
                    daysPresent   = att?.DaysPresent ?? 0,
                    daysAbsent    = att?.DaysAbsent  ?? 0,
                    daysLate      = att?.DaysLate    ?? 0,
                    schoolDays    = att?.SchoolDays  ?? 0,
                    percentage    = att?.Percentage
                });
            }).RequireAuthorization();

            // ─── /api/parent/children/{id}/scores ────────────────────
            app.MapGet("/api/parent/children/{studentId:int}/scores", async (
                int studentId, int term, string year, HttpContext ctx, PostgresDbContext db) =>
            {
                if (!IsRole(ctx, "parent")) return Results.Forbid();
                if (!IsChildOfCaller(ctx, studentId)) return Results.Forbid();

                string academicYear = string.IsNullOrWhiteSpace(year)
                    ? DateTime.Now.Year.ToString()
                    : year;

                var student = await db.Students.FirstOrDefaultAsync(s => s.Id == studentId);
                if (student == null) return Results.NotFound();

                var scores = await db.AcademicPerformances
                    .Where(a => a.StudentId == studentId && a.Term == term && a.AcademicYear == academicYear)
                    .ToListAsync();

                var subjects = await db.Subjects
                    .Where(s => s.SchoolId == student.SchoolId
                             && (s.ClassId == student.ClassId || s.ClassId == null))
                    .ToDictionaryAsync(s => s.Id, s => s.SubjectName);

                var list = scores.Select(sc => new
                {
                    subjectId   = sc.SubjectId,
                    subjectName = subjects.TryGetValue(sc.SubjectId, out var n) ? n : $"Subject {sc.SubjectId}",
                    finalScore  = sc.FinalScore ?? 0m,
                    grade       = sc.Grade ?? ""
                }).ToList();

                return Results.Ok(new
                {
                    studentId,
                    term,
                    academicYear,
                    scores = list
                });
            }).RequireAuthorization();

            // ─── /api/parent/children/{id}/daily-attendance ──────────
            app.MapGet("/api/parent/children/{studentId:int}/daily-attendance", async (
                int studentId, int days, HttpContext ctx, PostgresDbContext db) =>
            {
                if (!IsRole(ctx, "parent")) return Results.Forbid();
                if (!IsChildOfCaller(ctx, studentId)) return Results.Forbid();

                int daysCount = days > 0 ? Math.Min(days, 366) : 30;

                var rows = await db.DailyAttendances
                    .Where(a => a.StudentId == studentId)
                    .OrderByDescending(a => a.Date)
                    .Take(daysCount)
                    .ToListAsync();

                var list = rows.Select(r => new
                {
                    date    = r.Date.ToString("yyyy-MM-dd"),
                    status  = r.Status ?? "",
                    remarks = r.Remarks ?? ""
                }).ToList();

                return Results.Ok(new { studentId, daily = list });
            }).RequireAuthorization();

            // ─── /api/parent/children/{id}/attendance-summary ────────
            app.MapGet("/api/parent/children/{studentId:int}/attendance-summary", async (
                int studentId, string period, HttpContext ctx, PostgresDbContext db) =>
            {
                if (!IsRole(ctx, "parent")) return Results.Forbid();
                if (!IsChildOfCaller(ctx, studentId)) return Results.Forbid();

                DateTime today = DateTime.UtcNow.Date;
                DateTime from;
                if (string.Equals(period, "yearly", StringComparison.OrdinalIgnoreCase))
                    from = new DateTime(today.Year, 1, 1);
                else if (string.Equals(period, "monthly", StringComparison.OrdinalIgnoreCase))
                    from = new DateTime(today.Year, today.Month, 1);
                else
                    from = today.AddDays(-((int)today.DayOfWeek == 0 ? 6 : (int)today.DayOfWeek - 1));

                DateTime to = today.AddDays(1);

                var marks = await db.DailyAttendances
                    .Where(a => a.StudentId == studentId && a.Date >= from && a.Date < to)
                    .ToListAsync();

                int present = marks.Count(m => string.Equals(m.Status, "Present", StringComparison.OrdinalIgnoreCase));
                int absent  = marks.Count(m => string.Equals(m.Status, "Absent",  StringComparison.OrdinalIgnoreCase));
                int late    = marks.Count(m => string.Equals(m.Status, "Late",    StringComparison.OrdinalIgnoreCase));
                int total   = present + absent + late;

                return Results.Ok(new
                {
                    studentId,
                    period = string.IsNullOrWhiteSpace(period) ? "weekly" : period.ToLowerInvariant(),
                    from   = from.ToString("yyyy-MM-dd"),
                    to     = today.ToString("yyyy-MM-dd"),
                    present,
                    absent,
                    late,
                    total,
                    percentage = total == 0 ? (decimal?)null : (present + late) * 100m / total
                });
            }).RequireAuthorization();

            // ─── /api/parent/children/{id}/published-reports ─────────
            app.MapGet("/api/parent/children/{studentId:int}/published-reports", async (
                int studentId, HttpContext ctx, PostgresDbContext db) =>
            {
                if (!IsRole(ctx, "parent")) return Results.Forbid();
                if (!IsChildOfCaller(ctx, studentId)) return Results.Forbid();

                var rows = await (
                    from pr in db.PublishedReports
                    join prd in db.PublishedReportData on pr.Id equals prd.PublishedReportId
                    where prd.StudentId == studentId && !pr.IsRevoked
                    orderby pr.PublishedDateUtc descending
                    select new
                    {
                        pr.Id,
                        pr.AcademicYear,
                        pr.Term,
                        pr.PublishedDateUtc,
                        pr.PublishedByName,
                        prd.DataHash
                    }).ToListAsync();

                var list = rows.Select(r => new
                {
                    publishedReportId = r.Id,
                    academicYear      = r.AcademicYear ?? "",
                    term              = r.Term,
                    publishedDateUtc  = r.PublishedDateUtc,
                    publishedByName   = r.PublishedByName ?? "",
                    dataHash          = r.DataHash ?? ""
                }).ToList();

                return Results.Ok(new { studentId, reports = list, count = list.Count });
            }).RequireAuthorization();

            // ─── /api/parent/children/{id}/published-report ──────────
            app.MapGet("/api/parent/children/{studentId:int}/published-report", async (
                int studentId, int term, string year, HttpContext ctx, PostgresDbContext db) =>
            {
                if (!IsRole(ctx, "parent")) return Results.Forbid();
                if (!IsChildOfCaller(ctx, studentId)) return Results.Forbid();
                if (term < 1 || term > 3 || string.IsNullOrWhiteSpace(year))
                    return Results.BadRequest(new { error = "invalid_request" });

                var row = await (
                    from pr in db.PublishedReports
                    join prd in db.PublishedReportData on pr.Id equals prd.PublishedReportId
                    where prd.StudentId == studentId
                       && pr.AcademicYear == year
                       && pr.Term == term
                       && !pr.IsRevoked
                    orderby pr.PublishedDateUtc descending
                    select new
                    {
                        pr.PublishedDateUtc,
                        pr.PublishedByName,
                        prd.DataHash,
                        prd.DataJson
                    }).FirstOrDefaultAsync();

                if (row == null) return Results.NotFound(new { error = "not_published" });

                return Results.Ok(new
                {
                    studentId,
                    term,
                    year,
                    publishedDateUtc = row.PublishedDateUtc,
                    publishedByName  = row.PublishedByName ?? "",
                    dataHash         = row.DataHash ?? "",
                    dataJson         = row.DataJson ?? ""
                });
            }).RequireAuthorization();

            // ─── /api/parent/notifications ───────────────────────────
            app.MapGet("/api/parent/notifications", async (
                HttpContext ctx, PostgresDbContext db, bool? unreadOnly) =>
            {
                if (!IsRole(ctx, "parent")) return Results.Forbid();
                if (!TryGetSchoolAndUser(ctx, out int schoolId, out int userId)) return Results.Unauthorized();

                var q = db.Notifications
                    .Where(n => n.SchoolId == schoolId)
                    .Where(n => EF.Property<int>(n, "UserId") == userId);
                if (unreadOnly == true) q = q.Where(n => !EF.Property<bool>(n, "IsRead"));

                var rows = await q.OrderByDescending(n => n.SentDate).Take(200).ToListAsync();
                var list = rows.Select(n => new
                {
                    id             = n.Id,
                    syncId         = EF.Property<string>(n, "SyncId") ?? "",
                    type           = n.NotificationType ?? "",
                    title          = n.Title ?? "",
                    body           = n.Message ?? "",
                    payloadJson    = EF.Property<string>(n, "PayloadJson") ?? "",
                    createdDateUtc = (DateTime?)n.SentDate,
                    isRead         = EF.Property<bool>(n, "IsRead")
                }).ToList();

                return Results.Ok(new { notifications = list, count = list.Count });
            }).RequireAuthorization();

            // ─── /api/parent/notifications/{id}/mark-read ────────────
            app.MapPost("/api/parent/notifications/{id:int}/mark-read", async (
                int id, HttpContext ctx, PostgresDbContext db) =>
            {
                if (!IsRole(ctx, "parent")) return Results.Forbid();
                if (!TryGetSchoolAndUser(ctx, out int schoolId, out int userId)) return Results.Unauthorized();

                var n = await db.Notifications
                    .Where(x => x.Id == id && x.SchoolId == schoolId)
                    .Where(x => EF.Property<int>(x, "UserId") == userId)
                    .FirstOrDefaultAsync();
                if (n == null) return Results.NotFound();
                db.Entry(n).Property("IsRead").CurrentValue = true;
                await db.SaveChangesAsync();
                return Results.Ok(new { status = "read" });
            }).RequireAuthorization();
        }

        // ── helpers ─────────────────────────────────────────────────

        private static int[] GetChildIds(HttpContext ctx)
        {
            string raw = ctx.User.FindFirst("child_ids")?.Value ?? "";
            if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<int>();
            return raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                      .Select(s => int.TryParse(s, out int i) ? i : 0)
                      .Where(i => i > 0)
                      .ToArray();
        }

        private static bool IsChildOfCaller(HttpContext ctx, int studentId) =>
            GetChildIds(ctx).Contains(studentId);

        private static bool IsRole(HttpContext ctx, string role) =>
            string.Equals(ctx.User.FindFirst("role")?.Value, role, StringComparison.OrdinalIgnoreCase);

        private static bool TryGetSchoolAndUser(HttpContext ctx, out int schoolId, out int userId)
        {
            schoolId = 0;
            userId = 0;
            return int.TryParse(ctx.User.FindFirst("school_id")?.Value, out schoolId)
                && int.TryParse(ctx.User.FindFirst("user_id")?.Value, out userId)
                && schoolId > 0 && userId > 0;
        }
    }
}