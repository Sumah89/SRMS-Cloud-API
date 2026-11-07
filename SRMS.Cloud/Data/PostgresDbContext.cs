#nullable disable
using System;
using Microsoft.EntityFrameworkCore;
using SRMS.Desktop.Models;

namespace SRMS.Cloud.Data
{
    public class PostgresDbContext : DbContext
    {
        public PostgresDbContext(DbContextOptions<PostgresDbContext> options)
            : base(options) { }

        public DbSet<School> Schools { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Class> Classes { get; set; }
        public DbSet<Subject> Subjects { get; set; }
        public DbSet<Student> Students { get; set; }
        public DbSet<Parent> Parents { get; set; }
        public DbSet<AcademicPerformance> AcademicPerformances { get; set; }
        public DbSet<AffectiveScoreData> AffectiveScores { get; set; }
        public DbSet<PsychomotorScoreData> PsychomotorScores { get; set; }
        public DbSet<AttendanceData> Attendances { get; set; }
        public DbSet<AttendanceRecord> DailyAttendances { get; set; }
        public DbSet<SchoolReportConfig> SchoolReportConfigs { get; set; }
        public DbSet<SchoolPromotionCriteria> SchoolPromotionCriteria { get; set; }
        public DbSet<PromotionPath> PromotionPaths { get; set; }
        public DbSet<PromotionRecord> PromotionRecords { get; set; }
        public DbSet<PromotionBatch> PromotionBatches { get; set; }
        public DbSet<StudentEnrollment> StudentEnrollments { get; set; }
        public DbSet<StudentSpecialCase> StudentSpecialCases { get; set; }
        public DbSet<TeacherAccess> TeacherAccesses { get; set; }
        public DbSet<ParentSubscription> ParentSubscriptions { get; set; }
        public DbSet<Subscription> Subscriptions { get; set; }
        public DbSet<SubscriptionPlan> SubscriptionPlans { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<PublishedReport> PublishedReports { get; set; }
        public DbSet<PublishedReportDatum> PublishedReportData { get; set; }
        public DbSet<ScoreSubmission> ScoreSubmissions { get; set; }
        public DbSet<ScoreAuditLog> ScoreAuditLogs { get; set; }
        public DbSet<SyncChangeRow> ChangeLog { get; set; }
        public DbSet<SyncChangeReceipt> SyncChangeReceipts { get; set; }

        protected override void OnModelCreating(ModelBuilder b)
        {
            base.OnModelCreating(b);

            // ── Table names (PascalCase matches legacy SQLite schema) ──
            b.Entity<School>().ToTable("Schools");
            b.Entity<User>().ToTable("Users");
            b.Entity<Class>().ToTable("Classes");
            b.Entity<Subject>().ToTable("Subjects");
            b.Entity<Student>().ToTable("Students");
            b.Entity<Parent>().ToTable("Parents");
            b.Entity<AcademicPerformance>().ToTable("AcademicPerformance");
            b.Entity<AffectiveScoreData>().ToTable("AffectiveScores");
            b.Entity<PsychomotorScoreData>().ToTable("PsychomotorScores");
            b.Entity<AttendanceData>().ToTable("Attendance");
            b.Entity<AttendanceRecord>().ToTable("DailyAttendance");
            b.Entity<SchoolReportConfig>().ToTable("SchoolReportConfig");
            b.Entity<SchoolPromotionCriteria>().ToTable("SchoolPromotionCriteria");
            b.Entity<PromotionPath>().ToTable("PromotionPaths");
            b.Entity<PromotionRecord>().ToTable("PromotionRecords");
            b.Entity<PromotionBatch>().ToTable("PromotionBatches");
            b.Entity<StudentEnrollment>().ToTable("StudentEnrollments");
            b.Entity<StudentSpecialCase>().ToTable("StudentSpecialCase");
            b.Entity<TeacherAccess>().ToTable("TeacherAccess");
            b.Entity<ParentSubscription>().ToTable("ParentSubscriptions");
            b.Entity<Subscription>().ToTable("Subscriptions");
            b.Entity<SubscriptionPlan>().ToTable("SubscriptionPlans");
            b.Entity<Notification>().ToTable("Notifications");
            b.Entity<PublishedReport>().ToTable("PublishedReports");
            b.Entity<PublishedReportDatum>().ToTable("PublishedReportData");
            b.Entity<ScoreSubmission>().ToTable("ScoreSubmissions");
            b.Entity<ScoreAuditLog>().ToTable("ScoreAuditLog");
            b.Entity<SyncChangeRow>().ToTable("change_log");
            b.Entity<SyncChangeReceipt>().ToTable("SyncChangeReceipts");

            // ── Users ──────────────────────────────────────────────────
            b.Entity<User>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.Username).IsRequired().HasMaxLength(128);
                e.HasIndex(x => x.Username).IsUnique();
                e.Property(x => x.FullName).HasMaxLength(256);
                e.Property(x => x.Role).HasMaxLength(64);
                e.Property(x => x.Phone).HasMaxLength(32);
                e.Property(x => x.Email).HasMaxLength(256);
                e.Property(x => x.PasswordHash).IsRequired();
                e.Property(x => x.CreatedDate).HasColumnType("timestamp with time zone");
                e.Property(x => x.ModifiedDate).HasColumnType("timestamp with time zone");
                e.Property(x => x.LastLoginDate).HasColumnType("timestamp with time zone");
            });

            // ── Schools ────────────────────────────────────────────────
            b.Entity<School>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.SchoolName).IsRequired().HasMaxLength(256);
                e.Property(x => x.District).HasMaxLength(128);
                e.Property(x => x.Town).HasMaxLength(128);
                e.Property(x => x.Chiefdom).HasMaxLength(128);
                e.Property(x => x.Address).HasMaxLength(512);
                e.Property(x => x.Phone).HasMaxLength(32);
                e.Property(x => x.Email).HasMaxLength(256);
                e.Property(x => x.PrincipalName).HasMaxLength(256);
                e.Property(x => x.LogoPath).HasMaxLength(1024);
                e.Property(x => x.LicenseKey).HasMaxLength(256);
                e.Property(x => x.LicenseExpiryDate).HasColumnType("timestamp with time zone");
                e.Property(x => x.CreatedDate).HasColumnType("timestamp with time zone");
                e.Property(x => x.ModifiedDate).HasColumnType("timestamp with time zone");
                e.Property(x => x.ResultType).HasMaxLength(32).HasDefaultValue("FiveTests");
            });

            // ── Classes ────────────────────────────────────────────────
            b.Entity<Class>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.ClassName).IsRequired().HasMaxLength(128);
                e.Property(x => x.ClassLevel).HasMaxLength(64);
                e.Property(x => x.Stream).HasMaxLength(64);
                e.Property(x => x.AcademicYear).HasMaxLength(32);
                e.Property(x => x.ClassTeacherName).HasMaxLength(256);
                e.Property(x => x.ResultTypeOverride).HasMaxLength(32);
                e.Property(x => x.CreatedDate).HasColumnType("timestamp with time zone");
                e.Property(x => x.ModifiedDate).HasColumnType("timestamp with time zone");
            });

            // ── Subjects ───────────────────────────────────────────────
            b.Entity<Subject>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.SubjectName).IsRequired().HasMaxLength(128);
                e.Property(x => x.SubjectCode).HasMaxLength(32);
                e.Property(x => x.AcademicYear).HasMaxLength(32);
                e.Property(x => x.PassMark).HasColumnType("numeric(5,2)");
                e.Property(x => x.CreatedDate).HasColumnType("timestamp with time zone");
                e.Property(x => x.ModifiedDate).HasColumnType("timestamp with time zone");
            });

            // ── Students ───────────────────────────────────────────────
            b.Entity<Student>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.AdmissionNumber).IsRequired().HasMaxLength(64);
                e.HasIndex(x => x.AdmissionNumber).IsUnique();
                e.Property(x => x.FullName).IsRequired().HasMaxLength(256);
                e.Property(x => x.Gender).HasMaxLength(16);
                e.Property(x => x.Address).HasMaxLength(512);
                e.Property(x => x.ParentName).HasMaxLength(256);
                e.Property(x => x.ParentPhone).HasMaxLength(32);
                e.Property(x => x.HouseColour).HasMaxLength(64);
                e.Property(x => x.Stream).HasMaxLength(64);
                e.Property(x => x.PhotoPath).HasMaxLength(1024);
                e.Property(x => x.DateOfBirth).HasColumnType("timestamp with time zone");
                e.Property(x => x.CreatedDate).HasColumnType("timestamp with time zone");
                e.Property(x => x.ModifiedDate).HasColumnType("timestamp with time zone");
            });

            // ── Parents ────────────────────────────────────────────────
            b.Entity<Parent>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.PhoneE164).IsRequired().HasMaxLength(32);
                e.HasIndex(x => x.PhoneE164).IsUnique();
                e.Property(x => x.FullName).HasMaxLength(256);
                e.Property(x => x.DeviceId).HasMaxLength(256);
                e.Property(x => x.PinHash).HasMaxLength(512);
                e.Property(x => x.CreatedUtc).HasColumnType("timestamp with time zone");
                e.Property(x => x.UpdatedUtc).HasColumnType("timestamp with time zone");
            });

            // ── AcademicPerformance ────────────────────────────────────
            b.Entity<AcademicPerformance>(e =>
            {
                e.HasKey(x => x.Id);
                e.Ignore(x => x.FinalScore);    // <--- ADD THIS LINE
                e.Ignore(x => x.Term1Mean);     // <--- ADD THIS LINE
                e.Ignore(x => x.Term2Mean);     // <--- ADD THIS LINE
                e.Ignore(x => x.Term3Mean);     // <--- ADD THIS LINE
                e.Ignore(x => x.AnnualMean);    // <--- ADD THIS LINE
                e.Ignore(x => x.IsFiveTests);   // <--- ADD THIS LINE
                e.Ignore(x => x.IsSixTests);    // <--- ADD THIS LINE

                e.Property(x => x.T1).HasColumnType("numeric(5,2)");
                e.Property(x => x.T2).HasColumnType("numeric(5,2)");
                e.Property(x => x.T3).HasColumnType("numeric(5,2)");
                e.Property(x => x.T4).HasColumnType("numeric(5,2)");
                e.Property(x => x.T5).HasColumnType("numeric(5,2)");
                e.Property(x => x.T6).HasColumnType("numeric(5,2)");
                e.Property(x => x.Exam).HasColumnType("numeric(5,2)");
                e.Property(x => x.Percent40).HasColumnType("numeric(5,2)");
                e.Property(x => x.Total).HasColumnType("numeric(5,2)");
                e.Property(x => x.Grade).HasMaxLength(8);
                e.Property(x => x.Remarks).HasMaxLength(512);
                e.Property(x => x.AcademicYear).HasMaxLength(32);
                e.Property(x => x.CreatedDate).HasColumnType("timestamp with time zone");
                e.Property(x => x.ModifiedDate).HasColumnType("timestamp with time zone");
                e.HasIndex(x => new { x.StudentId, x.Term, x.AcademicYear });
            });

            // ── AffectiveScores ────────────────────────────────────────
            b.Entity<AffectiveScoreData>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.AcademicYear).HasMaxLength(32);
                e.Property(x => x.CreatedDate).HasColumnType("timestamp with time zone");
                e.Property(x => x.ModifiedDate).HasColumnType("timestamp with time zone");
                e.HasIndex(x => new { x.StudentId, x.Term, x.AcademicYear });
            });

            // ── PsychomotorScores ──────────────────────────────────────
            b.Entity<PsychomotorScoreData>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.AcademicYear).HasMaxLength(32);
                e.Property(x => x.CreatedDate).HasColumnType("timestamp with time zone");
                e.Property(x => x.ModifiedDate).HasColumnType("timestamp with time zone");
                e.HasIndex(x => new { x.StudentId, x.Term, x.AcademicYear });
            });

            // ── Attendance ─────────────────────────────────────────────
            b.Entity<AttendanceData>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.AcademicYear).HasMaxLength(32);
                e.Property(x => x.Percentage).HasColumnType("numeric(5,2)");
                e.Property(x => x.Term1Percentage).HasColumnType("numeric(5,2)");
                e.Property(x => x.Term2Percentage).HasColumnType("numeric(5,2)");
                e.Property(x => x.Term3Percentage).HasColumnType("numeric(5,2)");
                e.Property(x => x.AnnualPercentage).HasColumnType("numeric(5,2)");
                e.Property(x => x.CreatedDate).HasColumnType("timestamp with time zone");
                e.Property(x => x.ModifiedDate).HasColumnType("timestamp with time zone");
                e.HasIndex(x => new { x.StudentId, x.Term, x.AcademicYear });
            });

            // ── DailyAttendance ────────────────────────────────────────
            b.Entity<AttendanceRecord>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.Status).IsRequired().HasMaxLength(32);
                e.Property(x => x.AcademicYear).HasMaxLength(32);
                e.Property(x => x.Remarks).HasMaxLength(512);
                e.Property(x => x.Date).HasColumnType("timestamp with time zone");
                e.HasIndex(x => new { x.StudentId, x.AcademicYear });
                e.HasIndex(x => x.Date);
                e.HasIndex(x => new { x.StudentId, x.Date, x.AcademicYear }).IsUnique();
            });

            // ── SchoolReportConfig ─────────────────────────────────────
            b.Entity<SchoolReportConfig>(e =>
            {
                e.HasKey(x => x.Id);
                e.HasIndex(x => x.SchoolId).IsUnique();
                e.Property(x => x.ResultType).HasMaxLength(32);
                e.Property(x => x.PaperOrientation).HasMaxLength(16);
                e.Property(x => x.HeaderLogoPath).HasMaxLength(1024);
                e.Property(x => x.FooterText).HasMaxLength(1024);
                e.Property(x => x.CreatedDate).HasColumnType("timestamp with time zone");
                e.Property(x => x.ModifiedDate).HasColumnType("timestamp with time zone");
            });

            // ── SchoolPromotionCriteria ────────────────────────────────
            b.Entity<SchoolPromotionCriteria>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.CriteriaType).HasMaxLength(64);
                e.Property(x => x.MinAveragePercentage).HasColumnType("numeric(5,2)");
                e.Property(x => x.CoreSubjectPassMark).HasColumnType("numeric(5,2)");
                e.Property(x => x.OptionalSubjectPassMark).HasColumnType("numeric(5,2)");
                e.Property(x => x.TotalPassMark).HasColumnType("numeric(5,2)");
                e.Property(x => x.EnglishMathsPassMark).HasColumnType("numeric(5,2)");
                e.Property(x => x.MinAttendancePercentage).HasColumnType("numeric(5,2)");
                e.Property(x => x.ClassName).HasMaxLength(128);
                e.Property(x => x.CreatedDate).HasColumnType("timestamp with time zone");
                e.Property(x => x.ModifiedDate).HasColumnType("timestamp with time zone");
            });

            // ── PromotionPaths ─────────────────────────────────────────
            b.Entity<PromotionPath>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.FromClassName).IsRequired().HasMaxLength(128);
                e.Property(x => x.ToClassName).IsRequired().HasMaxLength(128);
                e.Property(x => x.CreatedDate).HasColumnType("timestamp with time zone");
                e.Property(x => x.ModifiedDate).HasColumnType("timestamp with time zone");
                e.HasIndex(x => new { x.SchoolId, x.FromClassName }).IsUnique();
            });

            // ── PromotionRecords ───────────────────────────────────────
            b.Entity<PromotionRecord>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.AcademicYear).HasMaxLength(32);
                e.Property(x => x.AnnualAverage).HasColumnType("numeric(5,2)");
                e.Property(x => x.NextClass).HasMaxLength(128);
                e.Property(x => x.Reason).HasMaxLength(1024);
                e.Property(x => x.ComputedByName).HasMaxLength(256);
                e.Property(x => x.OverrideReason).HasMaxLength(1024);
                e.Property(x => x.ComputedAt).HasColumnType("timestamp with time zone");
                e.Property(x => x.OverriddenAt).HasColumnType("timestamp with time zone");
                e.HasIndex(x => new { x.StudentId, x.AcademicYear }).IsUnique();
            });

            // ── PromotionBatches ───────────────────────────────────────
            b.Entity<PromotionBatch>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.SourceAcademicYear).HasMaxLength(32);
                e.Property(x => x.DestinationAcademicYear).HasMaxLength(32);
                e.Property(x => x.PerformedByName).HasMaxLength(256);
                e.Property(x => x.PerformedByRole).HasMaxLength(64);
                e.Property(x => x.RevertedByName).HasMaxLength(256);
                e.Property(x => x.BatchTimestamp).HasColumnType("timestamp with time zone");
                e.Property(x => x.RevertedAt).HasColumnType("timestamp with time zone");
                e.Ignore(x => x.SourceClassName);
                e.Ignore(x => x.DestinationClassName);
            });

            // ── StudentEnrollments ─────────────────────────────────────
            b.Entity<StudentEnrollment>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.AcademicYear).HasMaxLength(32);
                e.Property(x => x.Status).HasMaxLength(32);
                e.Property(x => x.ExitReason).HasMaxLength(512);
                e.Property(x => x.EnrollmentDate).HasColumnType("timestamp with time zone");
                e.Property(x => x.ExitDate).HasColumnType("timestamp with time zone");
                e.Property(x => x.CreatedDate).HasColumnType("timestamp with time zone");
                e.Property(x => x.ModifiedDate).HasColumnType("timestamp with time zone");
                e.Ignore(x => x.ClassName);
                e.HasIndex(x => new { x.StudentId, x.ClassId, x.AcademicYear }).IsUnique();
                e.HasIndex(x => new { x.StudentId, x.IsCurrent });
            });

            // ── StudentSpecialCase ─────────────────────────────────────
            b.Entity<StudentSpecialCase>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.AcademicYear).HasMaxLength(32);
                e.Property(x => x.ResultType).HasMaxLength(32);
                e.Property(x => x.CreatedDate).HasColumnType("timestamp with time zone");
                e.Property(x => x.ModifiedDate).HasColumnType("timestamp with time zone");
                e.HasIndex(x => new { x.StudentId, x.AcademicYear }).IsUnique();
            });

            // ── TeacherAccess ──────────────────────────────────────────
            b.Entity<TeacherAccess>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.AccessLevel).HasMaxLength(32);
                e.Property(x => x.TeacherName).HasMaxLength(256);
                e.Property(x => x.ClassName).HasMaxLength(128);
                e.Property(x => x.SubjectName).HasMaxLength(128);
                e.Property(x => x.AssignedDate).HasColumnType("timestamp with time zone");
            });

            // ── ParentSubscriptions ────────────────────────────────────
            // Core model uses ParentUserId (not ParentId). Unique index per parent.
            b.Entity<ParentSubscription>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.SyncId).IsRequired().HasMaxLength(64);
                e.HasIndex(x => x.SyncId).IsUnique();
                e.Property(x => x.PlanCode).HasMaxLength(32);
                e.Property(x => x.Currency).HasMaxLength(8);
                e.Property(x => x.Provider).HasMaxLength(64);
                e.Property(x => x.Status).HasMaxLength(32);
                e.Property(x => x.PaymentReference).HasMaxLength(128);
                e.Property(x => x.ProviderTransactionId).HasMaxLength(256);
                e.Property(x => x.Source).HasMaxLength(32);
                e.Property(x => x.Amount).HasColumnType("numeric(18,2)");
                e.Property(x => x.StartedUtc).HasColumnType("timestamp with time zone");
                e.Property(x => x.ExpiresUtc).HasColumnType("timestamp with time zone");
                e.Property(x => x.GraceUntilUtc).HasColumnType("timestamp with time zone");
                e.Property(x => x.CreatedUtc).HasColumnType("timestamp with time zone");
                e.Property(x => x.UpdatedUtc).HasColumnType("timestamp with time zone");
                e.HasIndex(x => x.ParentUserId).IsUnique();
                e.HasIndex(x => x.PaymentReference);
            });

            // ── Subscriptions ──────────────────────────────────────────
            b.Entity<Subscription>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.PlanCode).HasMaxLength(32);
                e.Property(x => x.Status).HasMaxLength(32);
                e.Property(x => x.Source).HasMaxLength(32);
                e.Property(x => x.LegacyLicenseKey).HasMaxLength(256);
                e.Property(x => x.StartedUtc).HasColumnType("timestamp with time zone");
                e.Property(x => x.ExpiresUtc).HasColumnType("timestamp with time zone");
                e.Property(x => x.GraceUntilUtc).HasColumnType("timestamp with time zone");
                e.Property(x => x.UpdatedUtc).HasColumnType("timestamp with time zone");
                e.HasIndex(x => x.SchoolId).IsUnique();
            });

            // ── SubscriptionPlans ──────────────────────────────────────
            b.Entity<SubscriptionPlan>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.Audience).IsRequired().HasMaxLength(32);
                e.Property(x => x.PlanCode).IsRequired().HasMaxLength(32);
                e.Property(x => x.DisplayName).HasMaxLength(128);
                e.Property(x => x.Currency).HasMaxLength(8);
                e.Property(x => x.Amount).HasColumnType("numeric(18,2)");
                e.Property(x => x.UpdatedUtc).HasColumnType("timestamp with time zone");
                e.HasIndex(x => new { x.Audience, x.PlanCode }).IsUnique();
            });

            // ── Notifications ──────────────────────────────────────────
            b.Entity<Notification>(e =>
            {
                e.HasKey(x => x.Id);
                e.Ignore(x => x.TargetRole);
                e.Ignore(x => x.TargetClassId);
                e.Ignore(x => x.TargetStudentId);
                e.Ignore(x => x.SentBy);
                e.Ignore(x => x.IsSent);
                e.Ignore(x => x.TotalRecipients);
                e.Ignore(x => x.SuccessfulDeliveries);

                e.Property(x => x.Title).HasMaxLength(256);
                e.Property(x => x.Message).HasMaxLength(2048).HasColumnName("Body");
                e.Property(x => x.NotificationType).HasMaxLength(64).HasColumnName("Type");
                e.Property(x => x.SentDate).HasColumnName("CreatedDateUtc");
                e.Property(x => x.IsActive).HasColumnName("IsRead");

                // Extra columns used by sync (not on Core model).
                e.Property<string>("SyncId").HasMaxLength(64);
                e.HasIndex("SyncId").IsUnique();
                e.Property<int>("UserId");
                e.Property<string>("PayloadJson");
                e.Property<bool>("IsPushed").HasDefaultValue(false);
                e.Property<DateTime?>("PushedUtc").HasColumnType("timestamp with time zone");
                
                // Shadow property for IsRead — define FIRST, then index
                e.Property<bool>("IsRead").HasDefaultValue(false);

                e.HasIndex("UserId");
                e.HasIndex("IsRead").HasDatabaseName("IX_Notifications_IsRead");
            });

            // ── PublishedReports ───────────────────────────────────────
            b.Entity<PublishedReport>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.SyncId).IsRequired().HasMaxLength(64);
                e.HasIndex(x => x.SyncId).IsUnique();
                e.Property(x => x.AcademicYear).HasMaxLength(32);
                e.Property(x => x.PublishedByName).HasMaxLength(256);
                e.Property(x => x.PublishedDateUtc).HasColumnType("timestamp with time zone");
                e.Property(x => x.RevokedDateUtc).HasColumnType("timestamp with time zone");
                e.HasIndex(x => new { x.SchoolId, x.ClassId, x.AcademicYear, x.Term }).IsUnique();
            });

            // ── PublishedReportData ────────────────────────────────────
            b.Entity<PublishedReportDatum>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.SyncId).IsRequired().HasMaxLength(64);
                e.HasIndex(x => x.SyncId).IsUnique();
                e.Property(x => x.DataHash).HasMaxLength(128);
                e.Property(x => x.CreatedDateUtc).HasColumnType("timestamp with time zone");
                e.HasIndex(x => x.PublishedReportId);
                e.HasIndex(x => x.StudentId);
            });

            // ── ScoreSubmissions ───────────────────────────────────────
            b.Entity<ScoreSubmission>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.SyncId).HasMaxLength(64);
                e.Property(x => x.AcademicYear).HasMaxLength(32);
                e.Property(x => x.SubmittedUtc).HasColumnType("timestamp with time zone");
                e.HasIndex(x => x.SyncId);
                e.HasIndex(x => new { x.SchoolId, x.TeacherId, x.ClassId, x.SubjectId, x.Term, x.AcademicYear }).IsUnique();
            });

            // ── ScoreAuditLog ──────────────────────────────────────────
            b.Entity<ScoreAuditLog>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.AcademicYear).HasMaxLength(32);
                e.Property(x => x.FieldName).HasMaxLength(32);
                e.Property(x => x.OldValue).HasMaxLength(2048);
                e.Property(x => x.NewValue).HasMaxLength(2048);
                e.Property(x => x.OperationType).HasMaxLength(32);
                e.Property(x => x.Source).HasMaxLength(128);
                e.Property(x => x.UserName).HasMaxLength(256);
                e.Property(x => x.UserRole).HasMaxLength(64);
                e.Property(x => x.Remarks).HasMaxLength(1024);
                e.Property(x => x.ChangedAt).HasColumnType("timestamp with time zone");
                e.HasIndex(x => new { x.StudentId, x.AcademicYear });
                e.HasIndex(x => new { x.SubjectId, x.Term, x.AcademicYear });
                e.HasIndex(x => x.ChangedAt);
            });

            // ── Sync: change_log ───────────────────────────────────────
            b.Entity<SyncChangeRow>(e =>
            {
                e.ToTable("change_log");
                e.HasKey(x => x.Seq);
                e.Property(x => x.Seq).ValueGeneratedOnAdd();
                e.Property(x => x.SyncUuid).IsRequired().HasMaxLength(64);
                e.HasIndex(x => x.SyncUuid).IsUnique();
                e.Property(x => x.TableName).IsRequired().HasMaxLength(128);
                e.Property(x => x.Op).IsRequired().HasMaxLength(16);
                e.Property(x => x.Payload).IsRequired();
                e.Property(x => x.UpdatedBy).HasMaxLength(128);
                e.HasIndex(x => x.Seq);
            });

            // ── Sync: SyncChangeReceipts ───────────────────────────────
            b.Entity<SyncChangeReceipt>(e =>
            {
                e.HasKey(x => x.SyncUuid);
                e.Property(x => x.SyncUuid).HasMaxLength(64);
                e.Property(x => x.TableName).IsRequired().HasMaxLength(128);
                e.Property(x => x.Op).IsRequired().HasMaxLength(16);
                e.Property(x => x.Payload).IsRequired();
                e.Property(x => x.ReceivedAt).HasColumnType("timestamp with time zone");
            });
        }
    }

    // ── Cloud-only entities ────────────────────────────────────────
    public class SubscriptionPlan
    {
        public int Id { get; set; }
        public string Audience { get; set; }
        public string PlanCode { get; set; }
        public string DisplayName { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "SLL";
        public int DurationDays { get; set; }
        public int GraceDays { get; set; } = 7;
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public int? UpdatedByUserId { get; set; }
    }

    public class PublishedReport
    {
        public int Id { get; set; }
        public string SyncId { get; set; }
        public int SchoolId { get; set; }
        public int ClassId { get; set; }
        public string AcademicYear { get; set; }
        public int Term { get; set; }
        public DateTime PublishedDateUtc { get; set; }
        public int PublishedByUserId { get; set; }
        public string PublishedByName { get; set; }
        public int SnapshotCount { get; set; }
        public bool IsRevoked { get; set; }
        public DateTime? RevokedDateUtc { get; set; }
        public int? RevokedByUserId { get; set; }
    }

    public class PublishedReportDatum
    {
        public int Id { get; set; }
        public string SyncId { get; set; }
        public int PublishedReportId { get; set; }
        public int StudentId { get; set; }
        public string DataJson { get; set; }
        public string DataHash { get; set; }
        public DateTime CreatedDateUtc { get; set; }
    }

    public class ScoreSubmission
    {
        public int Id { get; set; }
        public string SyncId { get; set; }
        public int SchoolId { get; set; }
        public int TeacherId { get; set; }
        public int ClassId { get; set; }
        public int SubjectId { get; set; }
        public int Term { get; set; }
        public string AcademicYear { get; set; }
        public DateTime SubmittedUtc { get; set; }
    }

    public class SyncChangeRow
    {
        public long Seq { get; set; }
        public string SyncUuid { get; set; }
        public string TableName { get; set; }
        public string Op { get; set; }
        public string Payload { get; set; }
        public long UpdatedAt { get; set; }
        public string UpdatedBy { get; set; }
    }

    public class SyncChangeReceipt
    {
        public string SyncUuid { get; set; }
        public string TableName { get; set; }
        public string Op { get; set; }
        public string Payload { get; set; }
        public bool Applied { get; set; }
        public DateTime ReceivedAt { get; set; }
    }
}