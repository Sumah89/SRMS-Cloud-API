#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SRMS.Cloud.Data;
using SRMS.Desktop.Security;

namespace SRMS.Cloud
{
    public sealed class AuthService
    {
        private readonly PostgresDbContext _db;

        public AuthService(PostgresDbContext db) { _db = db; }

        // ── admin login ────────────────────────────────────────────
        public async Task<AuthResult> AdminLoginAsync(string username, string password, string schoolName)
        {
            if (string.IsNullOrWhiteSpace(username)) return null;

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null) return null;
            if (!PasswordHasher.VerifyPassword(password, user.PasswordHash)) return null;
            if (user.Role != "Admin" && user.Role != "MasterAdmin") return null;

            // MasterAdmin: no school attached.
            if (user.Role == "MasterAdmin")
            {
                return new AuthResult
                {
                    UserId = user.Id,
                    SchoolId = user.SchoolId ?? 0,
                    Role = "admin",
                    FullName = user.FullName ?? "",
                    Children = new(),
                    TeacherClassIds = new()
                };
            }

            // Regular admin: must have a school
            if (!user.SchoolId.HasValue || user.SchoolId.Value <= 0) return null;
            var school = await _db.Schools.FirstOrDefaultAsync(s => s.Id == user.SchoolId.Value);
            if (school == null) return null;

            if (!string.IsNullOrWhiteSpace(schoolName)
                && !string.Equals(school.SchoolName?.Trim(), schoolName.Trim(),
                                   StringComparison.OrdinalIgnoreCase))
                return null;

            return new AuthResult
            {
                UserId = user.Id,
                SchoolId = user.SchoolId.Value,
                Role = "admin",
                FullName = user.FullName ?? "",
                Children = new(),
                TeacherClassIds = new()
            };
        }

        // ── teacher login (phone + 12-char code) ──────────────────
        public async Task<AuthResult> TeacherLoginAsync(string phoneE164, string code)
        {
            try
            {
                var codeRow = await _db.Database
                    .SqlQueryRaw<TeacherCodeRow>(
                        @"SELECT ""TeacherUserId"", ""SchoolId""
                          FROM ""TeacherCodesV2""
                          WHERE ""PhoneE164"" = {0}
                            AND ""Code""      = {1}
                            AND ""IsActive""  = TRUE
                          LIMIT 1",
                        phoneE164, code)
                    .FirstOrDefaultAsync();

                if (codeRow == null || codeRow.TeacherUserId <= 0) return null;

                var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == codeRow.TeacherUserId);
                if (user == null || !user.IsActive) return null;

                var classIds = await _db.TeacherAccesses
                    .Where(t => t.TeacherId == user.Id && t.IsActive && t.ClassId != null)
                    .Select(t => t.ClassId.Value)
                    .Distinct()
                    .ToListAsync();

                return new AuthResult
                {
                    UserId = user.Id,
                    SchoolId = codeRow.SchoolId,
                    Role = "teacher",
                    FullName = user.FullName ?? "",
                    Children = new(),
                    TeacherClassIds = classIds
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TeacherLoginAsync failed: {ex.Message}");
                return null;
            }
        }

        // ── parent login (phone + 12-char code) ──────────────────
        public async Task<AuthResult> ParentLoginAsync(string phoneE164, string code)
        {
            try
            {
                var rows = await _db.Database
                    .SqlQueryRaw<ParentCodeRow>(
                        @"SELECT ""ParentUserId"", ""SchoolId"", ""StudentId""
                          FROM ""ParentCodesV2""
                          WHERE ""PhoneE164"" = {0}
                            AND ""Code""      = {1}
                            AND ""IsActive""  = TRUE",
                        phoneE164, code)
                    .ToListAsync();

                if (rows.Count == 0) return null;

                int parentUserId = rows[0].ParentUserId;
                var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == parentUserId);
                if (user == null) return null;

                var children = new List<SRMS.Desktop.Sync.Grants.LinkedChild>();
                foreach (var r in rows)
                {
                    var s = await _db.Students.FirstOrDefaultAsync(x => x.Id == r.StudentId);
                    if (s == null) continue;
                    var school = await _db.Schools.FirstOrDefaultAsync(x => x.Id == s.SchoolId);
                    var cls    = await _db.Classes.FirstOrDefaultAsync(x => x.Id == s.ClassId);
                    children.Add(new SRMS.Desktop.Sync.Grants.LinkedChild
                    {
                        StudentId       = s.Id,
                        StudentName     = s.FullName ?? "",
                        AdmissionNumber = s.AdmissionNumber ?? "",
                        SchoolId        = s.SchoolId,
                        SchoolName      = school?.SchoolName ?? "",
                        ClassName       = cls?.ClassName ?? ""
                    });
                }

                return new AuthResult
                {
                    UserId = parentUserId,
                    SchoolId = children.Count > 0 ? children[0].SchoolId : 0,
                    Role = "parent",
                    FullName = user.FullName ?? "",
                    Children = children,
                    TeacherClassIds = new()
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ParentLoginAsync failed: {ex.Message}");
                return null;
            }
        }

        // ── parent PIN login ───────────────────────────────────────
        public async Task<AuthResult> ParentPinLoginAsync(string phoneE164, string pin, string deviceId)
        {
            try
            {
                var parent = await _db.Parents.FirstOrDefaultAsync(p => p.PhoneE164 == phoneE164);
                if (parent == null) return null;

                if (string.IsNullOrEmpty(parent.PinHash)
                    || !PasswordHasher.VerifyPassword(pin, parent.PinHash))
                    return null;

                if (string.IsNullOrEmpty(parent.DeviceId))
                {
                    parent.DeviceId  = deviceId;
                    parent.UpdatedUtc = DateTime.UtcNow;
                    await _db.SaveChangesAsync();
                }
                else if (!string.Equals(parent.DeviceId, deviceId, StringComparison.Ordinal))
                {
                    throw new UnauthorizedAccessException(
                        "This account is bound to a different device. Contact your school admin.");
                }

                if (!parent.IsSubscribed)
                    throw new UnauthorizedAccessException("Subscription required.");

                var childRows = await (
                    from s in _db.Students
                    join sch in _db.Schools on s.SchoolId equals sch.Id
                    join c in _db.Classes on s.ClassId equals c.Id into gj
                    from c in gj.DefaultIfEmpty()
                    where s.ParentPhone == phoneE164
                    orderby sch.SchoolName, s.FullName
                    select new
                    {
                        s.Id,
                        s.FullName,
                        s.AdmissionNumber,
                        s.SchoolId,
                        SchoolName = sch.SchoolName,
                        ClassName  = c != null ? c.ClassName : ""
                    }).ToListAsync();

                var children = new List<SRMS.Desktop.Sync.Grants.LinkedChild>();
                int firstSchoolId = 0;
                foreach (var row in childRows)
                {
                    if (firstSchoolId == 0) firstSchoolId = row.SchoolId;
                    children.Add(new SRMS.Desktop.Sync.Grants.LinkedChild
                    {
                        StudentId       = row.Id,
                        StudentName     = row.FullName ?? "",
                        AdmissionNumber = row.AdmissionNumber ?? "",
                        SchoolId        = row.SchoolId,
                        SchoolName      = row.SchoolName ?? "",
                        ClassName       = row.ClassName ?? ""
                    });
                }

                return new AuthResult
                {
                    UserId = parent.Id,
                    SchoolId = firstSchoolId,
                    Role = "parent",
                    FullName = parent.FullName ?? "Parent",
                    Children = children,
                    TeacherClassIds = new()
                };
            }
            catch (UnauthorizedAccessException) { throw; }
            catch (Exception ex)
            {
                Debug.WriteLine($"ParentPinLoginAsync failed: {ex.Message}");
                return null;
            }
        }
    }

    public sealed class AuthResult
    {
        public int UserId { get; set; }
        public int SchoolId { get; set; }
        public string Role { get; set; } = "";
        public string FullName { get; set; } = "";
        public List<SRMS.Desktop.Sync.Grants.LinkedChild> Children { get; set; } = new();
        public List<int> TeacherClassIds { get; set; } = new();
    }

    // ── raw-row DTOs for SqlQueryRaw ────────────────────────────────
    public sealed class TeacherCodeRow
    {
        public int TeacherUserId { get; set; }
        public int SchoolId { get; set; }
    }

    public sealed class ParentCodeRow
    {
        public int ParentUserId { get; set; }
        public int SchoolId { get; set; }
        public int StudentId { get; set; }
    }
}