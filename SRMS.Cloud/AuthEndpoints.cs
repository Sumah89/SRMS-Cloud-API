#nullable disable
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace SRMS.Cloud
{
    public static class AuthEndpoints
    {
        public static void Map(WebApplication app)
        {
            app.MapPost("/api/auth/admin", async (AdminBody b, AuthService auth, JwtIssuer jwt) =>
            {
                var r = await auth.AdminLoginAsync(b?.Username, b?.Password, b?.School);
                if (r == null) return Results.Unauthorized();

                var token = jwt.Issue(
                    r.UserId, r.SchoolId, r.Role, r.FullName,
                    teacherClassIds: r.TeacherClassIds?.ToArray());

                return Results.Ok(new
                {
                    token,
                    role     = r.Role,
                    userId   = r.UserId,
                    schoolId = r.SchoolId,
                    fullName = r.FullName
                });
            });

            app.MapPost("/api/auth/teacher", async (TeacherBody b, AuthService auth, JwtIssuer jwt) =>
            {
                var r = await auth.TeacherLoginAsync(b?.Phone, b?.Code);
                if (r == null) return Results.Unauthorized();

                var token = jwt.Issue(
                    r.UserId, r.SchoolId, r.Role, r.FullName,
                    teacherClassIds: r.TeacherClassIds?.ToArray());

                return Results.Ok(new
                {
                    token,
                    role     = r.Role,
                    userId   = r.UserId,
                    schoolId = r.SchoolId,
                    fullName = r.FullName,
                    classIds = r.TeacherClassIds
                });
            });

            app.MapPost("/api/auth/parent", async (ParentBody b, AuthService auth, JwtIssuer jwt) =>
            {
                try
                {
                    var r = await auth.ParentPinLoginAsync(b?.Phone, b?.Code, b?.DeviceId ?? "web");
                    if (r == null) return Results.Unauthorized();

                    var token = jwt.Issue(
                        r.UserId, r.SchoolId, r.Role, r.FullName,
                        childStudentIds: r.Children?.Select(c => c.StudentId).ToArray());

                    return Results.Ok(new
                    {
                        token,
                        role     = r.Role,
                        userId   = r.UserId,
                        schoolId = r.SchoolId,
                        fullName = r.FullName,
                        children = r.Children
                    });
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Results.Json(new { error = ex.Message }, statusCode: 403);
                }
            });
        }

        public sealed record AdminBody(string Username, string Password, string School);
        public sealed record TeacherBody(string Phone, string Code);
        public sealed record ParentBody(string Phone, string Code, string DeviceId);
    }
}