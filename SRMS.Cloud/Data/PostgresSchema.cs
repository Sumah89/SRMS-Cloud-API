#nullable disable
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SRMS.Cloud.Data
{
    public static class PostgresSchema
    {
        public static async Task EnsureAsync(PostgresDbContext db, ILogger logger)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));

            // ── STEP 1: Create ALL tables manually (EF Core + raw) ─────────
            // We cannot rely on EnsureCreated because some tables may exist.
            // We use "CREATE TABLE IF NOT EXISTS" for everything.

            // Create EF Core tables via EnsureCreated (safe if empty DB)
            try
            {
                await db.Database.MigrateAsync();
            }
            catch
            {
                try { await db.Database.EnsureCreatedAsync(); } catch { }
            }

            // Manually create EF tables that might be missing
            await CreateEfTablesManuallyAsync(db, logger);

            // Create raw tables
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

            // ── STEP 2: Seed SubscriptionPlans ────────────────────────
            try
            {
                // Force-create the SubscriptionPlans table if missing
                await db.Database.ExecuteSqlRawAsync(@"
                    CREATE TABLE IF NOT EXISTS ""SubscriptionPlans"" (
                        ""Id""              BIGSERIAL PRIMARY KEY,
                        ""Audience""        TEXT NOT NULL,
                        ""PlanCode""        TEXT NOT NULL,
                        ""DisplayName""     TEXT,
                        ""Amount""          NUMERIC(18,2) NOT NULL DEFAULT 0,
                        ""Currency""        TEXT DEFAULT 'SLL',
                        ""DurationDays""    INTEGER NOT NULL DEFAULT 30,
                        ""GraceDays""       INTEGER NOT NULL DEFAULT 7,
                        ""IsActive""        BOOLEAN NOT NULL DEFAULT TRUE,
                        ""SortOrder""       INTEGER NOT NULL DEFAULT 0,
                        ""UpdatedUtc""      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                        ""UpdatedByUserId"" INTEGER
                    )");

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

            // ── STEP 3: Rotate master admin ───────────────────────────
            await RotateMasterAdminAsync(db, logger);
        }

        private static async Task CreateEfTablesManuallyAsync(PostgresDbContext db, ILogger logger)
        {
            // Create Users, Schools, etc. if they don't exist
            // (EnsureCreated sometimes skips if some tables exist)
            var scripts = new[]
            {
                @"CREATE TABLE IF NOT EXISTS ""Users"" (
                    ""Id"" SERIAL PRIMARY KEY,
                    ""SchoolId"" INTEGER,
                    ""Username"" TEXT NOT NULL UNIQUE,
                    ""PasswordHash"" TEXT NOT NULL,
                    ""FullName"" TEXT,
                    ""Role"" TEXT,
                    ""Phone"" TEXT,
                    ""Email"" TEXT,
                    ""IsActive"" BOOLEAN DEFAULT TRUE,
                    ""LastLoginDate"" TIMESTAMPTZ,
                    ""IsDeleted"" BOOLEAN DEFAULT FALSE,
                    ""DeletedDate"" TIMESTAMPTZ,
                    ""DeletedBy"" TEXT,
                    ""PasswordResetRequired"" BOOLEAN DEFAULT FALSE,
                    ""CreatedDate"" TIMESTAMPTZ DEFAULT NOW(),
                    ""ModifiedDate"" TIMESTAMPTZ DEFAULT NOW()
                )",
                @"CREATE TABLE IF NOT EXISTS ""Schools"" (
                    ""Id"" SERIAL PRIMARY KEY,
                    ""SchoolName"" TEXT NOT NULL,
                    ""District"" TEXT,
                    ""Town"" TEXT,
                    ""Chiefdom"" TEXT,
                    ""Address"" TEXT,
                    ""Phone"" TEXT,
                    ""Email"" TEXT,
                    ""PrincipalName"" TEXT,
                    ""LogoPath"" TEXT,
                    ""LicenseKey"" TEXT,
                    ""LicenseExpiryDate"" TIMESTAMPTZ,
                    ""IsActive"" BOOLEAN DEFAULT TRUE,
                    ""ResultType"" TEXT DEFAULT 'FiveTests',
                    ""CreatedDate"" TIMESTAMPTZ DEFAULT NOW(),
                    ""ModifiedDate"" TIMESTAMPTZ DEFAULT NOW()
                )",
                @"CREATE TABLE IF NOT EXISTS ""Classes"" (
                    ""Id"" SERIAL PRIMARY KEY,
                    ""SchoolId"" INTEGER NOT NULL,
                    ""ClassName"" TEXT NOT NULL,
                    ""ClassLevel"" TEXT,
                    ""Stream"" TEXT,
                    ""AcademicYear"" TEXT,
                    ""ClassTeacherName"" TEXT,
                    ""IsActive"" BOOLEAN DEFAULT TRUE,
                    ""ResultTypeOverride"" TEXT,
                    ""IsDeleted"" BOOLEAN DEFAULT FALSE,
                    ""DeletedDate"" TIMESTAMPTZ,
                    ""DeletedBy"" TEXT,
                    ""CreatedDate"" TIMESTAMPTZ DEFAULT NOW(),
                    ""ModifiedDate"" TIMESTAMPTZ DEFAULT NOW()
                )",
                @"CREATE TABLE IF NOT EXISTS ""Students"" (
                    ""Id"" SERIAL PRIMARY KEY,
                    ""SchoolId"" INTEGER NOT NULL,
                    ""ClassId"" INTEGER NOT NULL,
                    ""AdmissionNumber"" TEXT NOT NULL UNIQUE,
                    ""FullName"" TEXT NOT NULL,
                    ""DateOfBirth"" TIMESTAMPTZ,
                    ""Gender"" TEXT,
                    ""Address"" TEXT,
                    ""ParentName"" TEXT,
                    ""ParentPhone"" TEXT,
                    ""HouseColour"" TEXT,
                    ""Stream"" TEXT,
                    ""PhotoPath"" TEXT,
                    ""IsActive"" BOOLEAN DEFAULT TRUE,
                    ""IsDeleted"" BOOLEAN DEFAULT FALSE,
                    ""DeletedDate"" TIMESTAMPTZ,
                    ""DeletedBy"" TEXT,
                    ""CreatedDate"" TIMESTAMPTZ DEFAULT NOW(),
                    ""ModifiedDate"" TIMESTAMPTZ DEFAULT NOW()
                )",
                @"CREATE TABLE IF NOT EXISTS ""Subjects"" (
                    ""Id"" SERIAL PRIMARY KEY,
                    ""SchoolId"" INTEGER NOT NULL,
                    ""SubjectCode"" TEXT,
                    ""SubjectName"" TEXT NOT NULL,
                    ""IsCore"" BOOLEAN DEFAULT FALSE,
                    ""ClassId"" INTEGER,
                    ""AcademicYear"" TEXT,
                    ""PassMark"" NUMERIC(5,2) DEFAULT 50,
                    ""IsActive"" BOOLEAN DEFAULT TRUE,
                    ""IsDeleted"" BOOLEAN DEFAULT FALSE,
                    ""DeletedDate"" TIMESTAMPTZ,
                    ""DeletedBy"" TEXT,
                    ""CreatedDate"" TIMESTAMPTZ DEFAULT NOW(),
                    ""ModifiedDate"" TIMESTAMPTZ DEFAULT NOW()
                )",
                @"CREATE TABLE IF NOT EXISTS ""Parents"" (
                    ""Id"" SERIAL PRIMARY KEY,
                    ""PhoneE164"" TEXT NOT NULL UNIQUE,
                    ""FullName"" TEXT,
                    ""DeviceId"" TEXT,
                    ""PinHash"" TEXT,
                    ""IsSubscribed"" BOOLEAN DEFAULT FALSE,
                    ""CreatedUtc"" TIMESTAMPTZ DEFAULT NOW(),
                    ""UpdatedUtc"" TIMESTAMPTZ DEFAULT NOW()
                )"
            };

            foreach (var script in scripts)
            {
                try
                {
                    await db.Database.ExecuteSqlRawAsync(script);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Failed to create table: {Script}", script.Substring(0, 50));
                }
            }
        }

        public static async Task RotateMasterAdminAsync(PostgresDbContext db, ILogger logger)
        {
            const string masterUsername = "Alimamy";
            const string masterFullName = "Master Administrator";
            const string masterRole     = "MasterAdmin";
            
            // Read from Environment Variable, fallback to default if not set
            string masterPassword = Environment.GetEnvironmentVariable("MASTER_ADMIN_PASSWORD") 
                                    ?? "@limamy@mara$umah";

            try
            {
                var legacy = await db.Users.Where(u => u.Username == "admin2").ToListAsync();
                if (legacy.Count > 0)
                {
                    db.Users.RemoveRange(legacy);
                    await db.SaveChangesAsync();
                    logger?.LogInformation("PostgresSchema: removed {Count} legacy 'admin2' account(s).", legacy.Count);
                }

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