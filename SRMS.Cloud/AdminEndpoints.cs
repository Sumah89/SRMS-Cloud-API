#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SRMS.Cloud.Data;
using SRMS.Desktop.Models;

namespace SRMS.Cloud
{
    public static class AdminEndpoints
    {
        public static void Map(WebApplication app)
        {
            // ══════════════════════════════════════════════════════════════════
            //  ONE-SHOT: rotate the master admin (Alimamy / @limamy@mara$umah)
            //  Deletes any stale "admin2" account and recreates the master.
            //  Idempotent. Safe to hit multiple times.
            // ══════════════════════════════════════════════════════════════════
            app.MapPost("/api/admin/rotate-master", async (PostgresDbContext db) =>
            {
                const string masterUsername = "Alimamy";
                const string masterPassword = "@limamy@mara$umah";
                const string masterFullName = "Master Administrator";
                const string masterRole     = "MasterAdmin";

                int removed = 0;
                var legacy = await db.Users.Where(u => u.Username == "admin2").ToListAsync();
                if (legacy.Count > 0)
                {
                    db.Users.RemoveRange(legacy);
                    removed = legacy.Count;
                    await db.SaveChangesAsync();
                }

                var hash = SRMS.Desktop.Security.PasswordHasher.HashPassword(masterPassword);
                var existing = await db.Users.FirstOrDefaultAsync(u => u.Username == masterUsername);

                int userId;
                bool created;
                if (existing == null)
                {
                    var user = new User
                    {
                        SchoolId     = null,
                        Username     = masterUsername,
                        PasswordHash = hash,
                        FullName     = masterFullName,
                        Role         = masterRole,
                        Phone        = "",
                        Email        = "",
                        IsActive     = true,
                        CreatedDate  = DateTime.UtcNow,
                        ModifiedDate = DateTime.UtcNow
                    };
                    db.Users.Add(user);
                    await db.SaveChangesAsync();
                    userId  = user.Id;
                    created = true;
                }
                else
                {
                    existing.PasswordHash = hash;
                    existing.FullName     = masterFullName;
                    existing.Role         = masterRole;
                    existing.IsActive     = true;
                    existing.SchoolId     = null;
                    existing.ModifiedDate = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                    userId  = existing.Id;
                    created = false;
                }

                return Results.Ok(new
                {
                    status              = "ok",
                    username            = masterUsername,
                    userId,
                    created,
                    legacyAdmin2Removed = removed,
                    regeneratedUtc      = DateTime.UtcNow
                });
            });

            // ══════════════════════════════════════════════════════════════════
            //  STATS
            // ══════════════════════════════════════════════════════════════════
            app.MapGet("/api/admin/stats", async (PostgresDbContext db) => Results.Ok(new
            {
                schools           = await db.Schools.CountAsync(),
                students          = await db.Students.CountAsync(),
                classes           = await db.Classes.CountAsync(),
                subjects          = await db.Subjects.CountAsync(),
                users             = await db.Users.CountAsync(),
                teacherCodes      = await SafeCountAsync(db, "TeacherCodesV2"),
                parentCodes       = await SafeCountAsync(db, "ParentCodesV2"),
                scores            = await db.AcademicPerformances.CountAsync(),
                notifications     = await db.Notifications.CountAsync(),
                publishedReports  = await db.PublishedReports.CountAsync(),
                subscriptionPlans = await db.SubscriptionPlans.CountAsync()
            }));

            // ══════════════════════════════════════════════════════════════════
            //  SAFE SELECT-ONLY QUERY (admin tool)
            // ══════════════════════════════════════════════════════════════════
            app.MapGet("/api/admin/query", async (string sql, PostgresDbContext db) =>
            {
                if (string.IsNullOrWhiteSpace(sql))
                    return Results.BadRequest(new { error = "empty_sql" });
                if (!sql.TrimStart().StartsWith("select", StringComparison.OrdinalIgnoreCase))
                    return Results.BadRequest(new { error = "only_select_allowed" });

                try
                {
                    var rows = new List<Dictionary<string, object>>();
                    await using var cmd = db.Database.GetDbConnection().CreateCommand();
                    cmd.CommandText = sql;
                    if (cmd.Connection.State != System.Data.ConnectionState.Open)
                        await cmd.Connection.OpenAsync();

                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var row = new Dictionary<string, object>();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        }
                        rows.Add(row);
                    }
                    return Results.Ok(new { rows, count = rows.Count });
                }
                catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
            });

            // ══════════════════════════════════════════════════════════════════
            //  ATTACH PARENT (12-char code + phone E164)
            // ══════════════════════════════════════════════════════════════════
            app.MapPost("/api/admin/attach-parent", async (AttachParentRequest req, PostgresDbContext db) =>
            {
                if (string.IsNullOrWhiteSpace(req.Phone)) return Results.BadRequest(new { error = "phone_required" });
                if (string.IsNullOrWhiteSpace(req.Code) || req.Code.Length != 12) return Results.BadRequest(new { error = "code_must_be_12_chars" });
                if (req.StudentId <= 0) return Results.BadRequest(new { error = "studentId_required" });

                string e164 = NormalizePhone(req.Phone);
                if (string.IsNullOrEmpty(e164)) return Results.BadRequest(new { error = "invalid_phone" });

                var student = await db.Students.FirstOrDefaultAsync(s => s.Id == req.StudentId);
                if (student == null) return Results.BadRequest(new { error = "student_not_found", studentId = req.StudentId });

                var parent = await db.Parents.FirstOrDefaultAsync(p => p.PhoneE164 == e164);
                if (parent == null)
                {
                    parent = new Parent
                    {
                        PhoneE164    = e164,
                        FullName     = req.FullName ?? "Parent",
                        IsSubscribed = false,
                        CreatedUtc   = DateTime.UtcNow,
                        UpdatedUtc   = DateTime.UtcNow
                    };
                    db.Parents.Add(parent);
                    await db.SaveChangesAsync();
                }

                string parentUsername = "parent_" + e164.Replace("+", "");
                var parentUser = await db.Users.FirstOrDefaultAsync(u => u.Username == parentUsername);
                if (parentUser == null)
                {
                    parentUser = new User
                    {
                        SchoolId     = student.SchoolId,
                        Username     = parentUsername,
                        PasswordHash = "",
                        FullName     = req.FullName ?? $"Parent of {student.FullName}",
                        Role         = "Parent",
                        Phone        = e164,
                        Email        = "",
                        IsActive     = true,
                        CreatedDate  = DateTime.UtcNow,
                        ModifiedDate = DateTime.UtcNow
                    };
                    db.Users.Add(parentUser);
                    await db.SaveChangesAsync();
                }

                string normalizedCode = req.Code.Trim().ToUpperInvariant();
                long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                var existingCodeId = await db.Database
                    .SqlQueryRaw<long?>(
                        "SELECT \"Id\" FROM \"ParentCodesV2\" WHERE \"ParentUserId\" = {0} AND \"StudentId\" = {1} LIMIT 1",
                        parentUser.Id, student.Id)
                    .FirstOrDefaultAsync();

                if (existingCodeId.HasValue && existingCodeId.Value > 0)
                {
                    await db.Database.ExecuteSqlRawAsync(
                        "UPDATE \"ParentCodesV2\" SET \"PhoneE164\" = {0}, \"Code\" = {1}, \"IsActive\" = TRUE WHERE \"Id\" = {2}",
                        e164, normalizedCode, existingCodeId.Value);
                }
                else
                {
                    await db.Database.ExecuteSqlRawAsync(
                        "INSERT INTO \"ParentCodesV2\" (\"ParentUserId\", \"SchoolId\", \"StudentId\", \"PhoneE164\", \"Code\", \"IsActive\", \"CreatedAt\") " +
                        "VALUES ({0}, {1}, {2}, {3}, {4}, TRUE, {5})",
                        parentUser.Id, student.SchoolId, student.Id, e164, normalizedCode, nowMs);
                }

                return Results.Ok(new
                {
                    status       = "ok",
                    studentId    = student.Id,
                    studentName  = student.FullName,
                    parentUserId = parentUser.Id,
                    phone        = e164,
                    code         = normalizedCode
                });
            });

            // ══════════════════════════════════════════════════════════════════
            //  BOOTSTRAP SCHOOL + ADMIN + SUBSCRIPTION
            // ══════════════════════════════════════════════════════════════════
            app.MapPost("/api/admin/bootstrap-school", async (BootstrapSchoolRequest req, PostgresDbContext db) =>
            {
                if (req == null
                    || string.IsNullOrWhiteSpace(req.SchoolName)
                    || string.IsNullOrWhiteSpace(req.Email)
                    || string.IsNullOrWhiteSpace(req.Password)
                    || string.IsNullOrWhiteSpace(req.FullName))
                    return Results.BadRequest(new { error = "schoolName, email, password, fullName required" });

                var existingSchool = await db.Schools
                    .FirstOrDefaultAsync(s => s.SchoolName == req.SchoolName);
                if (existingSchool != null)
                    return Results.BadRequest(new { error = "school_exists", schoolId = existingSchool.Id });

                var school = new School
                {
                    SchoolName   = req.SchoolName,
                    District     = req.District ?? "",
                    Town         = req.Town ?? "",
                    Chiefdom     = req.Chiefdom ?? "",
                    Phone        = req.Phone ?? "",
                    IsActive     = true,
                    ResultType   = "FiveTests",
                    CreatedDate  = DateTime.UtcNow,
                    ModifiedDate = DateTime.UtcNow
                };
                db.Schools.Add(school);
                await db.SaveChangesAsync();

                string pwHash = SRMS.Desktop.Security.PasswordHasher.HashPassword(req.Password);
                string username = req.Username ?? req.Email;

                var adminUser = await db.Users
                    .FirstOrDefaultAsync(u => u.Username == username || u.Email == req.Email);

                int userId;
                if (adminUser == null)
                {
                    adminUser = new User
                    {
                        SchoolId     = school.Id,
                        Username     = username,
                        PasswordHash = pwHash,
                        FullName     = req.FullName,
                        Role         = "Admin",
                        Phone        = req.Phone ?? "",
                        Email        = req.Email,
                        IsActive     = true,
                        CreatedDate  = DateTime.UtcNow,
                        ModifiedDate = DateTime.UtcNow
                    };
                    db.Users.Add(adminUser);
                    await db.SaveChangesAsync();
                    userId = adminUser.Id;
                }
                else
                {
                    adminUser.SchoolId     = school.Id;
                    adminUser.PasswordHash = pwHash;
                    adminUser.FullName     = req.FullName;
                    adminUser.Role         = "Admin";
                    adminUser.Email        = req.Email;
                    adminUser.IsActive     = true;
                    adminUser.ModifiedDate = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                    userId = adminUser.Id;
                }

                var plan = await db.SubscriptionPlans
                    .Where(p => p.Audience == "school" && p.PlanCode == "monthly" && p.IsActive)
                    .FirstOrDefaultAsync();
                int durationDays = plan?.DurationDays ?? 30;
                int graceDays    = plan?.GraceDays    ?? 7;

                DateTime nowUtc  = DateTime.UtcNow;
                DateTime expires = nowUtc.AddDays(durationDays);
                DateTime grace   = expires.AddDays(graceDays);

                var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.SchoolId == school.Id);
                if (sub == null)
                {
                    sub = new Subscription
                    {
                        SchoolId      = school.Id,
                        PlanCode      = "monthly",
                        Status        = "Active",
                        StartedUtc    = nowUtc,
                        ExpiresUtc    = expires,
                        GraceUntilUtc = grace,
                        Source        = "bootstrap",
                        UpdatedUtc    = nowUtc
                    };
                    db.Subscriptions.Add(sub);
                }
                else
                {
                    sub.PlanCode      = "monthly";
                    sub.Status        = "Active";
                    sub.StartedUtc    = nowUtc;
                    sub.ExpiresUtc    = expires;
                    sub.GraceUntilUtc = grace;
                    sub.Source        = "bootstrap";
                    sub.UpdatedUtc    = nowUtc;
                }
                await db.SaveChangesAsync();

                return Results.Ok(new
                {
                    status = "bootstrapped",
                    schoolId = school.Id,
                    userId,
                    email = req.Email,
                    subscription = new
                    {
                        planCode      = "monthly",
                        durationDays,
                        graceDays,
                        expiresUtc    = expires
                    }
                });
            });

            // ══════════════════════════════════════════════════════════════════
            //  PUBLISH CLASS REPORT CARDS
            // ══════════════════════════════════════════════════════════════════
            app.MapPost("/api/admin/publish",
                async (HttpContext ctx, PublishRequest req, PostgresDbContext db) =>
            {
                if (!IsRole(ctx, "admin")) return Results.Forbid();
                if (!int.TryParse(ctx.User.FindFirstValue("school_id"), out int schoolId) || schoolId <= 0)
                    return Results.Unauthorized();
                if (req == null || req.ClassId <= 0 || string.IsNullOrWhiteSpace(req.AcademicYear) || req.Term < 1 || req.Term > 3)
                    return Results.BadRequest(new { error = "invalid_request" });

                var school = await db.Schools.FirstOrDefaultAsync(s => s.Id == schoolId);
                if (school == null) return Results.NotFound(new { error = "school_not_found" });

                var cls = await db.Classes.FirstOrDefaultAsync(c => c.Id == req.ClassId);
                if (cls == null || cls.SchoolId != schoolId)
                    return Results.BadRequest(new { error = "class_not_in_school" });

                var students = await db.Students
                    .Where(s => s.SchoolId == schoolId && s.ClassId == req.ClassId)
                    .ToListAsync();
                if (students.Count == 0)
                    return Results.BadRequest(new { error = "no_students_in_class" });

                var existing = await db.PublishedReports
                    .FirstOrDefaultAsync(p => p.SchoolId == schoolId
                                           && p.ClassId == req.ClassId
                                           && p.AcademicYear == req.AcademicYear
                                           && p.Term == req.Term
                                           && !p.IsRevoked);

                int publishedId;
                DateTime publishedUtc = DateTime.UtcNow;

                if (existing != null)
                {
                    publishedId = existing.Id;
                    existing.PublishedDateUtc  = publishedUtc;
                    existing.PublishedByUserId = GetUserId(ctx);
                    existing.PublishedByName   = GetUserName(ctx) ?? "";
                    existing.SnapshotCount     = students.Count;
                    await db.SaveChangesAsync();

                    var oldSnaps = await db.PublishedReportData
                        .Where(d => d.PublishedReportId == publishedId)
                        .ToListAsync();
                    db.PublishedReportData.RemoveRange(oldSnaps);
                    await db.SaveChangesAsync();
                }
                else
                {
                    var report = new PublishedReport
                    {
                        SyncId            = Guid.NewGuid().ToString("N"),
                        SchoolId          = schoolId,
                        ClassId           = req.ClassId,
                        AcademicYear      = req.AcademicYear,
                        Term              = req.Term,
                        PublishedDateUtc  = publishedUtc,
                        PublishedByUserId = GetUserId(ctx),
                        PublishedByName   = GetUserName(ctx) ?? "",
                        SnapshotCount     = students.Count,
                        IsRevoked         = false
                    };
                    db.PublishedReports.Add(report);
                    await db.SaveChangesAsync();
                    publishedId = report.Id;
                }

                int snapshotsWritten = 0;
                foreach (var student in students)
                {
                    var snapshot = await BuildReportCardSnapshotAsync(
                        db, schoolId, req.ClassId, student.Id, req.Term, req.AcademicYear);
                    if (snapshot == null) continue;

                    string json = JsonSerializer.Serialize(snapshot);
                    string hash = ComputeSha256(json);

                    db.PublishedReportData.Add(new PublishedReportDatum
                    {
                        SyncId            = Guid.NewGuid().ToString("N"),
                        PublishedReportId = publishedId,
                        StudentId         = student.Id,
                        DataJson          = json,
                        DataHash          = hash,
                        CreatedDateUtc    = publishedUtc
                    });
                    snapshotsWritten++;
                }
                await db.SaveChangesAsync();

                return Results.Ok(new
                {
                    status              = "published",
                    publishedReportId   = publishedId,
                    studentsCount       = students.Count,
                    snapshotsWritten,
                    publishedDateUtc    = publishedUtc
                });
            }).RequireAuthorization();

            // ══════════════════════════════════════════════════════════════════
            //  UNPUBLISH
            // ══════════════════════════════════════════════════════════════════
            app.MapPost("/api/admin/unpublish",
                async (HttpContext ctx, PublishRequest req, PostgresDbContext db) =>
            {
                if (!IsRole(ctx, "admin")) return Results.Forbid();
                if (!int.TryParse(ctx.User.FindFirstValue("school_id"), out int schoolId) || schoolId <= 0)
                    return Results.Unauthorized();
                if (req == null || req.ClassId <= 0 || string.IsNullOrWhiteSpace(req.AcademicYear) || req.Term < 1 || req.Term > 3)
                    return Results.BadRequest(new { error = "invalid_request" });

                var existing = await db.PublishedReports
                    .FirstOrDefaultAsync(p => p.SchoolId == schoolId
                                           && p.ClassId == req.ClassId
                                           && p.AcademicYear == req.AcademicYear
                                           && p.Term == req.Term
                                           && !p.IsRevoked);
                if (existing == null) return Results.NotFound(new { error = "not_published" });

                existing.IsRevoked       = true;
                existing.RevokedDateUtc  = DateTime.UtcNow;
                existing.RevokedByUserId = GetUserId(ctx);
                await db.SaveChangesAsync();

                return Results.Ok(new { status = "unpublished", publishedReportId = existing.Id });
            }).RequireAuthorization();

            // ══════════════════════════════════════════════════════════════════
            //  NOTIFICATIONS (admin view) — uses shadow props for sync columns
            // ══════════════════════════════════════════════════════════════════
            app.MapGet("/api/admin/notifications",
                async (HttpContext ctx, PostgresDbContext db, int? userId) =>
            {
                if (!IsRole(ctx, "admin")) return Results.Forbid();
                if (!int.TryParse(ctx.User.FindFirstValue("school_id"), out int schoolId) || schoolId <= 0)
                    return Results.Unauthorized();

                var q = db.Notifications.Where(n => n.SchoolId == schoolId);
                if (userId.HasValue)
                    q = q.Where(n => EF.Property<int>(n, "UserId") == userId.Value);

                var rows = await q.OrderByDescending(n => n.SentDate).Take(200).ToListAsync();
                var list = rows.Select(n => new
                {
                    id             = n.Id,
                    syncId         = EF.Property<string>(n, "SyncId") ?? "",
                    userId         = EF.Property<int>(n, "UserId"),
                    type           = n.NotificationType ?? "",
                    title          = n.Title ?? "",
                    body           = n.Message ?? "",
                    payloadJson    = EF.Property<string>(n, "PayloadJson") ?? "",
                    createdDateUtc = (DateTime?)n.SentDate,
                    isRead         = EF.Property<bool>(n, "IsRead"),
                    isPushed       = EF.Property<bool>(n, "IsPushed")
                }).ToList();

                return Results.Ok(new { notifications = list, count = list.Count });
            }).RequireAuthorization();

            app.MapPost("/api/admin/notifications/{id:int}/mark-read",
                async (int id, HttpContext ctx, PostgresDbContext db) =>
            {
                if (!IsRole(ctx, "admin")) return Results.Forbid();
                if (!int.TryParse(ctx.User.FindFirstValue("school_id"), out int schoolId) || schoolId <= 0)
                    return Results.Unauthorized();

                var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.SchoolId == schoolId);
                if (n == null) return Results.NotFound();
                db.Entry(n).Property("IsRead").CurrentValue = true;
                await db.SaveChangesAsync();
                return Results.Ok(new { status = "read" });
            }).RequireAuthorization();

            app.MapPost("/api/admin/notifications/mark-pushed",
                async (NotificationPushedRequest req, HttpContext ctx, PostgresDbContext db) =>
            {
                if (!IsRole(ctx, "admin")) return Results.Forbid();
                if (req?.SyncIds == null || req.SyncIds.Count == 0)
                    return Results.BadRequest(new { error = "syncIds_required" });

                int marked = 0;
                foreach (var sid in req.SyncIds)
                {
                    if (string.IsNullOrWhiteSpace(sid)) continue;
                    var n = await db.Notifications
                        .Where(x => EF.Property<string>(x, "SyncId") == sid)
                        .FirstOrDefaultAsync();
                    if (n == null) continue;

                    var pushedEntry = db.Entry(n).Property("IsPushed");
                    if ((bool)pushedEntry.CurrentValue) continue;

                    pushedEntry.CurrentValue = true;
                    db.Entry(n).Property("PushedUtc").CurrentValue = DateTime.UtcNow;
                    marked++;
                }
                if (marked > 0) await db.SaveChangesAsync();
                return Results.Ok(new { marked });
            }).RequireAuthorization();

            // ══════════════════════════════════════════════════════════════════
            //  PARENT MANAGEMENT (uses ParentUserId, not ParentId)
            // ══════════════════════════════════════════════════════════════════
            app.MapGet("/api/admin/parents", async (HttpContext ctx, PostgresDbContext db) =>
            {
                if (!IsRole(ctx, "admin")) return Results.Forbid();

                var parentsWithSub = await (
                    from p in db.Parents
                    join ps in db.ParentSubscriptions on p.Id equals ps.ParentUserId into gj
                    from ps in gj.DefaultIfEmpty()
                    orderby p.CreatedUtc descending
                    select new { p, ps }).ToListAsync();

                var phoneSet = parentsWithSub.Select(x => x.p.PhoneE164).Distinct().ToList();
                var childCounts = await db.Students
                    .Where(s => phoneSet.Contains(s.ParentPhone))
                    .GroupBy(s => s.ParentPhone)
                    .Select(g => new { Phone = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.Phone, x => x.Count);

                var list = parentsWithSub.Select(x => new
                {
                    parentId     = x.p.Id,
                    fullName     = x.p.FullName ?? "Unknown",
                    phone        = x.p.PhoneE164 ?? "",
                    deviceId     = string.IsNullOrEmpty(x.p.DeviceId) ? null : x.p.DeviceId,
                    isSubscribed = x.p.IsSubscribed,
                    planCode     = x.ps?.PlanCode,
                    expiresUtc   = x.ps?.ExpiresUtc,
                    childCount   = childCounts.TryGetValue(x.p.PhoneE164 ?? "", out var c) ? c : 0,
                    schools      = "See school list on child"
                }).ToList();

                return Results.Ok(new { parents = list });
            }).RequireAuthorization();

            app.MapPost("/api/admin/parents/{parentId:int}/reset-device",
                async (int parentId, HttpContext ctx, PostgresDbContext db) =>
            {
                if (!IsRole(ctx, "admin")) return Results.Forbid();
                var p = await db.Parents.FirstOrDefaultAsync(x => x.Id == parentId);
                if (p == null) return Results.NotFound();
                p.DeviceId   = null;
                p.UpdatedUtc = DateTime.UtcNow;
                await db.SaveChangesAsync();
                return Results.Ok(new { status = "reset" });
            }).RequireAuthorization();

            app.MapPost("/api/admin/parents/{parentId:int}/revoke-sub",
                async (int parentId, HttpContext ctx, PostgresDbContext db) =>
            {
                if (!IsRole(ctx, "admin")) return Results.Forbid();
                var p = await db.Parents.FirstOrDefaultAsync(x => x.Id == parentId);
                if (p != null)
                {
                    p.IsSubscribed = false;
                    p.UpdatedUtc   = DateTime.UtcNow;
                }
                var subs = await db.ParentSubscriptions
                    .Where(s => s.ParentUserId == parentId)
                    .ToListAsync();
                foreach (var s in subs)
                {
                    s.Status     = "Expired";
                    s.ExpiresUtc = DateTime.UtcNow;
                    s.UpdatedUtc = DateTime.UtcNow;
                }
                await db.SaveChangesAsync();
                return Results.Ok(new { status = "revoked" });
            }).RequireAuthorization();
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Helpers
        // ══════════════════════════════════════════════════════════════════════

        private static bool IsRole(HttpContext ctx, string role) =>
            string.Equals(ctx.User.FindFirstValue("role"), role, StringComparison.OrdinalIgnoreCase);

        private static int GetUserId(HttpContext ctx) =>
            int.TryParse(ctx.User.FindFirstValue("user_id"), out var id) ? id : 0;

        private static string GetUserName(HttpContext ctx) =>
            ctx.User.FindFirstValue("full_name") ?? "";

        private static async Task<object> BuildReportCardSnapshotAsync(
            PostgresDbContext db, int schoolId, int classId, int studentId, int term, string academicYear)
        {
            try
            {
                var student = await db.Students.FirstOrDefaultAsync(s => s.Id == studentId);
                if (student == null) return null;

                var config    = await db.SchoolReportConfigs.FirstOrDefaultAsync(c => c.SchoolId == schoolId);
                var subjects  = await db.Subjects
                    .Where(s => s.SchoolId == schoolId && (s.ClassId == classId || s.ClassId == null))
                    .ToListAsync();
                var scores    = await db.AcademicPerformances
                    .Where(a => a.StudentId == studentId && a.Term == term && a.AcademicYear == academicYear)
                    .ToListAsync();
                var attendance = await db.Attendances
                    .FirstOrDefaultAsync(a => a.StudentId == studentId && a.Term == term && a.AcademicYear == academicYear);

                var subjectRows = new List<object>();
                decimal totalScore = 0m;
                int passedCount = 0;
                foreach (var sub in subjects)
                {
                    var sc = scores.FirstOrDefault(s => s.SubjectId == sub.Id);
                    decimal fs = sc?.FinalScore ?? 0m;
                    totalScore += fs;
                    decimal passMark = sub.PassMark > 0 ? sub.PassMark : 50m;
                    bool passed = fs >= passMark;
                    if (passed) passedCount++;
                    subjectRows.Add(new
                    {
                        subjectId   = sub.Id,
                        subjectName = sub.SubjectName,
                        isCore      = sub.IsCore,
                        passMark,
                        finalScore  = fs,
                        grade       = sc?.Grade ?? "",
                        isPassed    = passed
                    });
                }
                decimal annualAverage = subjects.Count > 0 ? totalScore / subjects.Count : 0m;

                return new
                {
                    studentId,
                    fullName         = student.FullName,
                    admissionNumber  = student.AdmissionNumber,
                    gender           = student.Gender,
                    className        = (await db.Classes.FirstOrDefaultAsync(c => c.Id == classId))?.ClassName ?? "",
                    schoolId,
                    term,
                    academicYear,
                    generatedAtUtc   = DateTime.UtcNow,
                    config = new
                    {
                        resultType          = config?.ResultType ?? "FiveTests",
                        numberOfTerms       = config?.NumberOfTerms ?? 3,
                        paperOrientation    = config?.PaperOrientation ?? "Portrait",
                        showBehaviour       = config?.ShowBehaviourSection ?? true,
                        showPsychomotor     = config?.ShowPsychomotorSection ?? true,
                        showAttendance      = config?.ShowAttendanceSection ?? true,
                        showSubjectAnalysis = config?.ShowSubjectAnalysis ?? true,
                        footerText          = config?.FooterText ?? ""
                    },
                    attendance = new
                    {
                        daysPresent = attendance?.DaysPresent ?? 0,
                        daysAbsent  = attendance?.DaysAbsent  ?? 0,
                        daysLate    = attendance?.DaysLate    ?? 0,
                        schoolDays  = attendance?.SchoolDays  ?? 0,
                        percentage  = attendance?.Percentage
                    },
                    subjects         = subjectRows,
                    annualAverage,
                    passedCount,
                    totalSubjects    = subjects.Count
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BuildReportCardSnapshot] {studentId}: {ex.Message}");
                return null;
            }
        }

        private static string ComputeSha256(string value)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(value ?? "");
            var hash = sha.ComputeHash(bytes);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static async Task<long> SafeCountAsync(PostgresDbContext db, string table)
        {
            try
            {
                var count = await db.Database
                    .SqlQueryRaw<long>($"SELECT COUNT(*) AS \"Value\" FROM \"{table}\"")
                    .FirstOrDefaultAsync();
                return count;
            }
            catch { return -1; }
        }

        private static string NormalizePhone(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";
            var sb = new StringBuilder();
            foreach (char c in input) if (char.IsDigit(c)) sb.Append(c);
            string d = sb.ToString();
            if (d.StartsWith("232") && d.Length > 3) d = d.Substring(3);
            while (d.Length > 0 && d[0] == '0') d = d.Substring(1);
            if (d.Length != 8) return "";
            return "+232" + d;
        }
    }

    // ── Request records ──────────────────────────────────────────────────
    public record AttachParentRequest(int StudentId, string Phone, string Code, string FullName);
    public record PublishRequest(int ClassId, string AcademicYear, int Term);
    public record NotificationPushedRequest(List<string> SyncIds);

    public record BootstrapSchoolRequest(
        string SchoolName,
        string District,
        string Town,
        string Chiefdom,
        string Phone,
        string Email,
        string Username,
        string Password,
        string FullName);
}