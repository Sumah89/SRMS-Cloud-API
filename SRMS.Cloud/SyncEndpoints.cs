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
    public static class SyncEndpoints
    {
        public static void Map(WebApplication app)
        {
            app.MapPost("/sync/push",
                async (HttpContext ctx, ChangeApplier applier, PostgresDbContext db,
                       SRMS.Desktop.Sync.Protocol.PushRequest req) =>
            {
                if (!TryGetSchoolAndRole(ctx, out int schoolId, out string role))
                    return Results.Unauthorized();

                if (req?.Changes == null || req.Changes.Count == 0)
                    return Results.Ok(new SRMS.Desktop.Sync.Protocol.PushResponse
                    {
                        AcceptedCount   = 0,
                        ReceiverLastSeq = await GetMaxSeqAsync(db)
                    });

                int accepted = 0;
                foreach (var c in req.Changes)
                {
                    var change = new ChangeRow(c.Seq, c.SyncUuid, c.TableName, c.Op, c.Payload, c.UpdatedAt, c.UpdatedBy);
                    if (!await applier.ApplyAsync(change, schoolId, role)) continue;
                    accepted++;
                    await RecordInChangeLogAsync(db, c);
                }

                return Results.Ok(new SRMS.Desktop.Sync.Protocol.PushResponse
                {
                    AcceptedCount   = accepted,
                    ReceiverLastSeq = await GetMaxSeqAsync(db)
                });
            }).RequireAuthorization();

            app.MapPost("/sync/pull",
                async (HttpContext ctx, PostgresDbContext db,
                       SRMS.Desktop.Sync.Protocol.PullRequest req) =>
            {
                if (!TryGetSchoolAndRole(ctx, out _, out _)) return Results.Unauthorized();

                long afterSeq = req?.AfterSeq ?? 0;
                int batchSize = req?.BatchSize ?? 500;
                if (batchSize <= 0) batchSize = 500;
                if (batchSize > 1000) batchSize = 1000;

                var rows = await db.ChangeLog
                    .Where(r => r.Seq > afterSeq)
                    .OrderBy(r => r.Seq)
                    .Take(batchSize)
                    .ToListAsync();

                var changes = rows.Select(r => new SRMS.Desktop.Sync.Protocol.SyncChangeRow
                {
                    Seq       = r.Seq,
                    SyncUuid  = r.SyncUuid ?? "",
                    TableName = r.TableName ?? "",
                    Op        = r.Op ?? "",
                    Payload   = r.Payload ?? "",
                    UpdatedAt = r.UpdatedAt,
                    UpdatedBy = r.UpdatedBy ?? ""
                }).ToList();

                long newLast = changes.Count > 0 ? changes.Max(x => x.Seq) : afterSeq;

                return Results.Ok(new SRMS.Desktop.Sync.Protocol.PullResponse
                {
                    Changes    = changes,
                    NewLastSeq = newLast
                });
            }).RequireAuthorization();

            app.MapGet("/sync/health",
                async (HttpContext ctx, PostgresDbContext db) =>
            {
                if (!TryGetSchoolAndRole(ctx, out _, out _)) return Results.Unauthorized();
                return Results.Ok(new
                {
                    status         = "ok",
                    serverLastSeq  = await GetMaxSeqAsync(db),
                    schemaVersion  = await ReadSchemaVersionAsync(db),
                    serverUtc      = DateTime.UtcNow
                });
            }).RequireAuthorization();
        }

        private static bool TryGetSchoolAndRole(HttpContext ctx, out int schoolId, out string role)
        {
            schoolId = 0; role = "";
            if (!int.TryParse(ctx.User.FindFirstValue("school_id"), out schoolId) || schoolId <= 0) return false;
            role = ctx.User.FindFirstValue("role") ?? "";
            return !string.IsNullOrEmpty(role);
        }

        private static async Task RecordInChangeLogAsync(
            PostgresDbContext db, SRMS.Desktop.Sync.Protocol.SyncChangeRow c)
        {
            try
            {
                db.ChangeLog.Add(new SyncChangeRow
                {
                    SyncUuid  = c.SyncUuid,
                    TableName = c.TableName,
                    Op        = c.Op,
                    Payload   = c.Payload,
                    UpdatedAt = c.UpdatedAt,
                    UpdatedBy = c.UpdatedBy
                });
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SyncEndpoints] change_log insert failed: {ex.Message}");
            }
        }

        private static async Task<long> GetMaxSeqAsync(PostgresDbContext db)
        {
            try { return await db.ChangeLog.MaxAsync(r => (long?)r.Seq) ?? 0L; }
            catch { return 0L; }
        }

        private static async Task<int> ReadSchemaVersionAsync(PostgresDbContext db)
        {
            try
            {
                var result = await db.Database
                    .SqlQueryRaw<string>(
                        @"SELECT ""SettingValue"" AS ""Value""
                          FROM ""AppSettings""
                          WHERE ""SettingKey"" = 'SyncSchemaVersion'
                          LIMIT 1")
                    .FirstOrDefaultAsync();
                return int.TryParse(result, out var v) ? v : 0;
            }
            catch { return 0; }
        }
    }
}