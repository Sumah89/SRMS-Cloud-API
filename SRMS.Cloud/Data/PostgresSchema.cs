#nullable disable
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SRMS.Cloud.Data
{
    /// <summary>
    /// One-shot PostgreSQL schema bootstrap for tables not modeled by EF
    /// (legacy codes, sync receipts) and seed data (subscription plans).
    /// Every method is idempotent and safe to call on every startup.
    /// </summary>
    public static class PostgresSchema
    {
        public static async Task EnsureAsync(PostgresDbContext db, ILogger logger)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));

            // ── STEP 1: Create all EF-Core mapped tables ───────────────
            try
            {
                await db.Database.MigrateAsync();
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "PostgresSchema: MigrateAsync skipped. Attempting EnsureCreated fallback.");
                try 
                { 
                    await db.Database.EnsureCreatedAsync(); 
                }
                catch (Exception inner) 
                { 
                    logger?.LogError(inner, "PostgresSchema: EnsureCreated also failed."); 
                    throw; 
                }
            }

            // ── STEP 2: Create raw tables for endpoints (safe IF NOT EXISTS) ────
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""ParentCodesV2"" (
                    ""Id""           BIGSERIAL PRIMARY KEY,
                    ""ParentUserId"" INTEGER NOT NULL,
                    ""SchoolId""     INTEGER NOT NULL,
                    ""StudentId""    INTEGER NOT NULL,
                    ""PhoneE164""    TEXT NOT NULL,
                    ""Code""         TEXT NOT NULL,
                    ""IsActive""     BOOLEAN NOT NULL DEFAULT TRUE,
                    ""CreatedAt""    BIGINT NOT NULL
                )");
            await db.Database.ExecuteSqlRawAsync(
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ParentCodesV2_Parent_Student""
                  ON ""ParentCodesV2"" (""ParentUserId"", ""StudentId"")");
            await db.Database.ExecuteSqlRawAsync(
                @"CREATE INDEX IF NOT EXISTS ""IX_ParentCodesV2_Phone_Code""
                  ON ""ParentCodesV2"" (""PhoneE164"", ""Code"")");

            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""TeacherCodesV2"" (
                    ""Id""             BIGSERIAL PRIMARY KEY,
                    ""TeacherUserId""  INTEGER NOT NULL,
                    ""SchoolId""       INTEGER NOT NULL,
                    ""PhoneE164""      TEXT NOT NULL,
                    ""Code""           TEXT NOT NULL,
                    ""IsActive""       BOOLEAN NOT NULL DEFAULT TRUE,
                    ""CreatedAt""      BIGINT NOT NULL
                )");
            await db.Database.ExecuteSqlRawAsync(
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TeacherCodesV2_Phone_Code""
                  ON ""TeacherCodesV2"" (""PhoneE164"", ""Code"")");

            // ── STEP 3: Seed default subscription plans ────────────────
            try
            {
                long planCount = await db.SubscriptionPlans.LongCountAsync();
                if (planCount == 0)
                {
                    DateTime now = DateTime.UtcNow;
                    db.SubscriptionPlans.AddRange(
                        new SubscriptionPlan { Audience = "school", PlanCode = "monthly", DisplayName = "Monthly — Le 50",  Amount = 50m,  Currency = "SLL", DurationDays = 30,  GraceDays = 7, IsActive = true, SortOrder = 10, UpdatedUtc = now },
                        new SubscriptionPlan { Audience = "school", PlanCode = "annual",  DisplayName = "Annual — Le 500",  Amount = 500m, Currency = "SLL", DurationDays = 365, GraceDays = 7, IsActive = true, SortOrder = 20, UpdatedUtc = now },
                        new SubscriptionPlan { Audience = "parent", PlanCode = "monthly", DisplayName = "Monthly — Le 50",  Amount = 50m,  Currency = "SLL", DurationDays = 30,  GraceDays = 7, IsActive = true, SortOrder = 10, UpdatedUtc = now },
                        new SubscriptionPlan { Audience = "parent", PlanCode = "annual",  DisplayName = "Annual — Le 500",  Amount = 500m, Currency = "SLL", DurationDays = 365, GraceDays = 7, IsActive = true, SortOrder = 20, UpdatedUtc = now }
                    );
                    await db.SaveChangesAsync();
                    logger?.LogInformation("PostgresSchema: seeded 4 default subscription plans.");
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "PostgresSchema: failed to seed subscription plans.");
                throw;
            }

            // ── STEP 4: Rotate master admin (idempotent) ──────────────
            await RotateMasterAdminAsync(db, logger);
        }

        public static async Task RotateMasterAdminAsync(PostgresDbContext db, ILogger logger)
        {
            const string masterUsername = "Alimamy";
            const string masterPassword = "@limamy@mara$umah";
            const string masterFullName = "Master Administrator";
            const string masterRole     = "MasterAdmin";

            try
            {
                // 1. Delete legacy admin2 if present
                var legacy = await db.Users.Where(u => u.Username == "admin2").ToListAsync();
                if (legacy.Count > 0)
                {
                    db.Users.RemoveRange(legacy);
                    await db.SaveChangesAsync();
                    logger?.LogInformation("PostgresSchema: removed {Count} legacy 'admin2' account(s).", legacy.Count);
                }

                // 2. Upsert master
                string hash = SRMS.Desktop.Security.PasswordHasher.HashPassword(masterPassword);
                var existing = await db.Users.FirstOrDefaultAsync(u => u.Username == masterUsername);

                if (existing == null)
                {
                    db.Users.Add(new SRMS.Desktop.Models.User
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
                    });
                    await db.SaveChangesAsync();
                    logger?.LogInformation("PostgresSchema: created master admin '{User}'.", masterUsername);
                }
                else
                {
                    bool changed = false;
                    if (existing.PasswordHash != hash) { existing.PasswordHash = hash; changed = true; }
                    if (existing.FullName != masterFullName) { existing.FullName = masterFullName; changed = true; }
                    if (existing.Role != masterRole) { existing.Role = masterRole; changed = true; }
                    if (!existing.IsActive) { existing.IsActive = true; changed = true; }
                    if (existing.SchoolId.HasValue) { existing.SchoolId = null; changed = true; }

                    if (changed)
                    {
                        existing.ModifiedDate = DateTime.UtcNow;
                        await db.SaveChangesAsync();
                        logger?.LogInformation("PostgresSchema: refreshed master admin '{User}' credentials.", masterUsername);
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "PostgresSchema.RotateMasterAdminAsync failed.");
                throw;
            }
        }
    }
}