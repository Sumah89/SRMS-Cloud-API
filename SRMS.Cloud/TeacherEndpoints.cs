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
using SRMS.Desktop.Models;

namespace SRMS.Cloud
{
    public static class TeacherEndpoints
    {
        public static void Map(WebApplication app)
        {
            // ────────────────────────────────────────────────────────
            //  GET /api/teacher/access
            // ────────────────────────────────────────────────────────
            app.MapGet("/api/teacher/access", async (HttpContext context, PostgresDbContext db) =>
            {
                if (!IsTeacher(context)) return Results.Forbid();
                if (!int.TryParse(context.User.FindFirstValue("user_id"), out var teacherId)
                    || !int.TryParse(context.User.FindFirstValue("school_id"), out var schoolId))
                    return Results.Unauthorized();

                var rows = await (
                    from ta in db.TeacherAccesses
                    where ta.TeacherId == teacherId && ta.IsActive
                    join c in db.Classes on ta.ClassId equals c.Id into cg
                    from c in cg.DefaultIfEmpty()
                    join s in db.Subjects on ta.SubjectId equals s.Id into sg
                    from s in sg.DefaultIfEmpty()
                    orderby c.ClassName, s.SubjectName
                    select new
                    {
                        ClassId    = (int?)ta.ClassId,
                        ClassName  = c != null ? c.ClassName : "",
                        ClassLevel = c != null ? c.ClassLevel : "",
                        Stream     = c != null ? c.Stream : "",
                        SubjectId  = (int?)ta.SubjectId,
                        SubjectName= s != null ? s.SubjectName : "",
                        AccessLevel= ta.AccessLevel
                    }).ToListAsync();

                var access = rows.Select(r => new
                {
                    classId       = r.ClassId,
                    className     = r.ClassName ?? "",
                    classLevel    = r.ClassLevel ?? "",
                    stream        = r.Stream ?? "",
                    subjectId     = r.SubjectId,
                    subjectName   = r.SubjectName ?? "",
                    accessLevel   = string.IsNullOrWhiteSpace(r.AccessLevel) ? "ReadOnly" : r.AccessLevel,
                    assignmentType= "SubjectTeacher"
                }).ToList();

                return Results.Ok(new { access });
            }).RequireAuthorization();

            // ────────────────────────────────────────────────────────
            //  GET /api/teacher/score-status
            // ────────────────────────────────────────────────────────
            app.MapGet("/api/teacher/score-status", async (
                HttpContext ctx, PostgresDbContext db,
                int classId, int subjectId, int term, string year) =>
            {
                if (!IsTeacher(ctx)) return Results.Forbid();
                if (!TryGetSchoolAndUser(ctx, out int schoolId, out _)) return Results.Unauthorized();
                if (classId <= 0 || subjectId <= 0 || term < 1 || term > 3 || string.IsNullOrWhiteSpace(year))
                    return Results.BadRequest(new { error = "invalid_request" });

                var sub = await db.ScoreSubmissions
                    .Where(s => s.SchoolId == schoolId
                             && s.ClassId == classId
                             && s.SubjectId == subjectId
                             && s.Term == term
                             && s.AcademicYear == year)
                    .FirstOrDefaultAsync();

                return Results.Ok(new
                {
                    isSubmitted          = sub != null,
                    submittedByTeacherId = sub?.TeacherId,
                    submittedUtc         = sub?.SubmittedUtc
                });
            }).RequireAuthorization();

            // ────────────────────────────────────────────────────────
            //  POST /api/teacher/score-submit
            // ────────────────────────────────────────────────────────
            app.MapPost("/api/teacher/score-submit", async (
                HttpContext ctx, PostgresDbContext db, ScoreSubmitRequest req) =>
            {
                if (!IsTeacher(ctx)) return Results.Forbid();
                if (!TryGetSchoolAndUser(ctx, out int schoolId, out int userId)) return Results.Unauthorized();
                if (req == null || req.ClassId <= 0 || req.SubjectId <= 0
                    || req.Term < 1 || req.Term > 3
                    || string.IsNullOrWhiteSpace(req.AcademicYear))
                    return Results.BadRequest(new { error = "invalid_request" });

                bool already = await db.ScoreSubmissions.AnyAsync(s =>
                    s.SchoolId == schoolId
                    && s.TeacherId == userId
                    && s.ClassId == req.ClassId
                    && s.SubjectId == req.SubjectId
                    && s.Term == req.Term
                    && s.AcademicYear == req.AcademicYear);

                if (already) return Results.BadRequest(new { error = "already_submitted" });

                db.ScoreSubmissions.Add(new ScoreSubmission
                {
                    SyncId       = Guid.NewGuid().ToString("N"),
                    SchoolId     = schoolId,
                    TeacherId    = userId,
                    ClassId      = req.ClassId,
                    SubjectId    = req.SubjectId,
                    Term         = req.Term,
                    AcademicYear = req.AcademicYear,
                    SubmittedUtc = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
                return Results.Ok(new { status = "submitted" });
            }).RequireAuthorization();

            // ────────────────────────────────────────────────────────
            //  POST /api/teacher/score-sheet
            // ────────────────────────────────────────────────────────
            app.MapPost("/api/teacher/score-sheet", async (
                HttpContext ctx, PostgresDbContext db,
                int classId, int subjectId, int term, string year) =>
            {
                if (!IsTeacher(ctx)) return Results.Forbid();
                if (!TryGetSchoolAndUser(ctx, out int schoolId, out int userId)) return Results.Unauthorized();
                if (classId <= 0 || subjectId <= 0 || term < 1 || term > 3 || string.IsNullOrWhiteSpace(year))
                    return Results.BadRequest(new { error = "invalid_request" });

                if (!await TeacherHasAccessAsync(db, userId, classId, subjectId))
                    return Results.Forbid();

                var sub = await db.ScoreSubmissions
                    .Where(s => s.SchoolId == schoolId
                             && s.ClassId == classId
                             && s.SubjectId == subjectId
                             && s.Term == term
                             && s.AcademicYear == year)
                    .FirstOrDefaultAsync();

                var students = await db.Students
                    .Where(s => s.SchoolId == schoolId && s.ClassId == classId)
                    .OrderBy(s => s.FullName)
                    .ToListAsync();

                var studentIds = students.Select(s => s.Id).ToList();
                var scores = await db.AcademicPerformances
                    .Where(a => studentIds.Contains(a.StudentId)
                             && a.SubjectId == subjectId
                             && a.Term == term
                             && a.AcademicYear == year)
                    .ToListAsync();

                var scoreById = scores.ToDictionary(a => a.StudentId);

                var rows = students.Select(s =>
                {
                    scoreById.TryGetValue(s.Id, out var sc);
                    return new
                    {
                        studentId       = s.Id,
                        fullName        = s.FullName,
                        admissionNumber = s.AdmissionNumber,
                        t1              = sc?.T1,
                        t2              = sc?.T2,
                        t3              = sc?.T3,
                        t4              = sc?.T4,
                        t5              = sc?.T5,
                        t6              = sc?.T6,
                        exam            = sc?.Exam,
                        finalScore      = sc?.FinalScore ?? 0m,
                        grade           = sc?.Grade ?? ""
                    };
                }).ToList();

                return Results.Ok(new
                {
                    classId, subjectId, term,
                    academicYear         = year,
                    isSubmitted          = sub != null,
                    submittedUtc         = sub?.SubmittedUtc,
                    submittedByTeacherId = sub?.TeacherId,
                    students             = rows
                });
            }).RequireAuthorization();

            // ────────────────────────────────────────────────────────
            //  GET /api/teacher/notifications
            //  Reads shadow props UserId / SyncId / PayloadJson / IsRead.
            // ────────────────────────────────────────────────────────
            app.MapGet("/api/teacher/notifications", async (
                HttpContext ctx, PostgresDbContext db, bool? unreadOnly) =>
            {
                if (!IsTeacher(ctx)) return Results.Forbid();
                if (!TryGetSchoolAndUser(ctx, out int schoolId, out int userId)) return Results.Unauthorized();

                var q = db.Notifications
                    .Where(n => n.SchoolId == schoolId)
                    .Where(n => EF.Property<int>(n, "UserId") == userId);
                if (unreadOnly == true)
                    q = q.Where(n => !EF.Property<bool>(n, "IsRead"));

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

            // ────────────────────────────────────────────────────────
            //  POST /api/teacher/notifications/{id}/mark-read
            // ────────────────────────────────────────────────────────
            app.MapPost("/api/teacher/notifications/{id:int}/mark-read", async (
                int id, HttpContext ctx, PostgresDbContext db) =>
            {
                if (!IsTeacher(ctx)) return Results.Forbid();
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

            // ────────────────────────────────────────────────────────
            //  GET /api/teacher/roll-call
            // ────────────────────────────────────────────────────────
            app.MapGet("/api/teacher/roll-call", async (
                HttpContext ctx, PostgresDbContext db,
                int classId, string date, string year) =>
            {
                if (!IsTeacher(ctx)) return Results.Forbid();
                if (!TryGetSchoolAndUser(ctx, out int schoolId, out int userId)) return Results.Unauthorized();
                if (classId <= 0 || string.IsNullOrWhiteSpace(date) || string.IsNullOrWhiteSpace(year))
                    return Results.BadRequest(new { error = "invalid_request" });

                if (!await TeacherHasAccessAsync(db, userId, classId, null))
                    return Results.Forbid();

                if (!DateTime.TryParse(date, out var day))
                    return Results.BadRequest(new { error = "invalid_date" });

                var students = await db.Students
                    .Where(s => s.SchoolId == schoolId && s.ClassId == classId)
                    .OrderBy(s => s.FullName)
                    .ToListAsync();

                var studentIds = students.Select(s => s.Id).ToList();
                var dayStart   = day.Date;
                var dayEnd     = dayStart.AddDays(1);

                var marks = await db.DailyAttendances
                    .Where(a => studentIds.Contains(a.StudentId)
                             && a.AcademicYear == year
                             && a.Date >= dayStart
                             && a.Date <  dayEnd)
                    .ToListAsync();

                var markByStudent = marks.ToDictionary(a => a.StudentId);

                var rows = students.Select(s =>
                {
                    markByStudent.TryGetValue(s.Id, out var m);
                    return new
                    {
                        studentId       = s.Id,
                        fullName        = s.FullName,
                        admissionNumber = s.AdmissionNumber,
                        status          = m?.Status ?? ""
                    };
                }).ToList();

                return Results.Ok(new
                {
                    classId,
                    date = day.ToString("yyyy-MM-dd"),
                    year,
                    students = rows
                });
            }).RequireAuthorization();

            // ────────────────────────────────────────────────────────
            //  POST /api/teacher/roll-call
            // ────────────────────────────────────────────────────────
            app.MapPost("/api/teacher/roll-call", async (
                HttpContext ctx, PostgresDbContext db, RollCallRequest req) =>
            {
                if (!IsTeacher(ctx)) return Results.Forbid();
                if (!TryGetSchoolAndUser(ctx, out int schoolId, out int userId)) return Results.Unauthorized();
                if (req == null || req.ClassId <= 0 || string.IsNullOrWhiteSpace(req.Date)
                    || string.IsNullOrWhiteSpace(req.AcademicYear))
                    return Results.BadRequest(new { error = "invalid_request" });

                if (!await TeacherHasAccessAsync(db, userId, req.ClassId, null))
                    return Results.Forbid();

                if (!DateTime.TryParse(req.Date, out var day))
                    return Results.BadRequest(new { error = "invalid_date" });

                if (req.Statuses == null || req.Statuses.Count == 0)
                    return Results.BadRequest(new { error = "no_statuses" });

                var studentIds = req.Statuses.Keys.ToList();
                var validIds = await db.Students
                    .Where(s => studentIds.Contains(s.Id) && s.ClassId == req.ClassId && s.SchoolId == schoolId)
                    .Select(s => s.Id)
                    .ToListAsync();
                var validSet = new HashSet<int>(validIds);

                var dayStart = day.Date;
                var dayEnd   = dayStart.AddDays(1);

                var existing = await db.DailyAttendances
                    .Where(a => validIds.Contains(a.StudentId)
                             && a.AcademicYear == req.AcademicYear
                             && a.Date >= dayStart
                             && a.Date <  dayEnd)
                    .ToListAsync();
                var existingByStudent = existing.ToDictionary(a => a.StudentId);

                int saved = 0;
                foreach (var kv in req.Statuses)
                {
                    if (!validSet.Contains(kv.Key)) continue;
                    if (string.IsNullOrWhiteSpace(kv.Value)) continue;

                    if (existingByStudent.TryGetValue(kv.Key, out var row))
                    {
                        row.Status  = kv.Value;
                        row.Term    = req.Term;
                        row.Remarks = null;
                        saved++;
                    }
                    else
                    {
                        db.DailyAttendances.Add(new AttendanceRecord
                        {
                            StudentId    = kv.Key,
                            Date         = day,
                            Status       = kv.Value,
                            AcademicYear = req.AcademicYear,
                            Term         = req.Term,
                            Remarks      = null
                        });
                        saved++;
                    }
                }

                if (saved > 0) await db.SaveChangesAsync();
                return Results.Ok(new { saved });
            }).RequireAuthorization();
        }

        // ── helpers ──────────────────────────────────────────────────

        private static bool IsTeacher(HttpContext context) =>
            string.Equals(context.User.FindFirstValue("role"), "teacher", StringComparison.OrdinalIgnoreCase);

        private static bool TryGetSchoolAndUser(HttpContext ctx, out int schoolId, out int userId)
        {
            schoolId = 0;
            userId = 0;
            return int.TryParse(ctx.User.FindFirstValue("school_id"), out schoolId)
                && int.TryParse(ctx.User.FindFirstValue("user_id"), out userId)
                && schoolId > 0 && userId > 0;
        }

        /// <summary>
        /// A teacher has access to (classId, subjectId) if there is an active
        /// TeacherAccess row. Pass subjectId = null to mean "any subject in the class".
        /// </summary>
        private static async Task<bool> TeacherHasAccessAsync(
            PostgresDbContext db, int teacherId, int classId, int? subjectId)
        {
            var q = db.TeacherAccesses.Where(t =>
                t.TeacherId == teacherId
                && t.ClassId == classId
                && t.IsActive);
            if (subjectId.HasValue) q = q.Where(t => t.SubjectId == subjectId.Value);
            return await q.AnyAsync();
        }
    }

    public record ScoreSubmitRequest(int ClassId, int SubjectId, int Term, string AcademicYear);
    public record RollCallRequest(int ClassId, string Date, string AcademicYear, int? Term,
                                  Dictionary<int, string> Statuses);
}