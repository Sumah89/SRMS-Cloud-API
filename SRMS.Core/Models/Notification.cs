using System;

namespace SRMS.Desktop.Models
{
    public class Notification
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public string NotificationType { get; set; } // Announcement, Result, Fee, Attendance
        public string TargetRole { get; set; } // All, Teachers, Parents, Students
        public int? TargetClassId { get; set; }
        public int? TargetStudentId { get; set; }
        public int SentBy { get; set; }
        public DateTime SentDate { get; set; }
        public bool IsSent { get; set; }
        public bool IsActive { get; set; }
        public int TotalRecipients { get; set; }
        public int SuccessfulDeliveries { get; set; }
    }

    public class DeviceRegistration
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }
        public int? UserId { get; set; }
        public int? StudentId { get; set; }
        public string DeviceToken { get; set; }
        public string DeviceType { get; set; } // iOS, Android, Web
        public string DeviceModel { get; set; }
        public string OsVersion { get; set; }
        public string AppVersion { get; set; }
        public bool IsActive { get; set; }
        public DateTime RegisteredDate { get; set; }
        public DateTime LastActiveDate { get; set; }
    }

    public class ParentDevice
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public string DeviceToken { get; set; }
        public string DeviceType { get; set; }
        public int SchoolId { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
    }
}