using System;
using System.Collections.Generic;

namespace SRMS.Desktop.Models
{
    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public T Data { get; set; }
    }

    public class TeacherLoginResponse
    {
        public bool Success { get; set; }
        public int UserId { get; set; }
        public string Username { get; set; }
        public string FullName { get; set; }
        public string Role { get; set; }
        public string Token { get; set; }
        public bool ExamActive { get; set; }
        public bool CanEdit { get; set; }
        public bool HasAccess { get; set; }
        public int RemainingMinutes { get; set; }
    }

    public class ParentLoginResponse
    {
        public bool Success { get; set; }
        public int StudentId { get; set; }
        public string StudentName { get; set; }
        public string AdmissionNumber { get; set; }
        public string ClassName { get; set; }
        public string Token { get; set; }
    }

    public class SessionStatusResponse
    {
        public bool Success { get; set; }
        public bool IsActive { get; set; }
        public int RemainingMinutes { get; set; }
        public DateTime EndTime { get; set; }
    }

    public class HealthResponse
    {
        public string Status { get; set; }
        public DateTime Timestamp { get; set; }
        public bool ExamActive { get; set; }
        public DateTime ExamEndTime { get; set; }
        public string ApiVersion { get; set; }
    }

    public class NgrokStatusResponse
    {
        public bool Success { get; set; }
        public bool IsRunning { get; set; }
        public string PublicUrl { get; set; }
    }

    public class StudentScoreDto
    {
        public int SubjectId { get; set; }
        public string SubjectName { get; set; }
        public decimal? T1 { get; set; }
        public decimal? T2 { get; set; }
        public decimal? T3 { get; set; }
        public decimal? T4 { get; set; }
        public decimal? T5 { get; set; }
        public decimal? T6 { get; set; }
        public decimal? Exam { get; set; }
        public decimal FinalScore { get; set; }
        public string Grade { get; set; }
    }
}