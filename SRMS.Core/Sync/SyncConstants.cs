using System;
using System.Collections.Generic;

namespace SRMS.Desktop.Sync
{
    public static class SyncConstants
    {
        public const int SchemaVersion = 3;   // do NOT bump — every install would re-migrate and break

        public const string SchemaVersionKey = "SyncSchemaVersion";

        public const string ColSyncUuid = "sync_uuid";
        public const string ColUpdatedAt = "updated_at";
        public const string ColUpdatedBy = "updated_by";

        public const string TableDeviceInfo = "DeviceInfo";
        public const string TableChangeLog = "change_log";
        public const string TableSyncPeers = "sync_peers";
        public const string TableConflictLog = "conflict_log";

        public static readonly IReadOnlyList<string> SyncableTables = new List<string>
        {
            // Reference data
            "Schools",
            "Classes",
            "Subjects",
            "Users",
            "Students",
            "StudentEnrollment",

            // Scores
            "AcademicPerformance",
            "AffectiveScores",
            "PsychomotorScores",
            "Attendance",
            "DailyAttendance",

            // Config
            "SchoolPromotionCriteria",
            "PromotionPaths",
            "SchoolReportConfig",
            "StudentSpecialCase",
            "PromotionRecord",
            "ScoreAuditLog",

            // Access control
            "AccessGrants",

            // Messaging
            "Messages",
            "MessageReplies",
            "ReportPublications",
            "DeviceTokens",

            // Phase 3b — access codes
            "TeacherCodes",
            "ParentCodesV2",

            // Parent subscription state (may not exist in older DBs — schema skips missing tables)
            "ParentSubscriptions",
        };

        public static readonly IReadOnlyList<string> NonSyncableTables = new List<string>
        {
            "TrialAnalytics",
            "AppSettings",
            "SchoolSettings",
            "StudentSettings",
            TableDeviceInfo,
            TableChangeLog,
            TableSyncPeers,
            TableConflictLog,
        };

        public const int SystemDefaultSchoolId = 0;
    }
}