using System;
using System.Threading.Tasks;

namespace SRMS.Desktop.Abstractions
{
    public interface ISessionStore
    {
        Task<SavedSession> LoadAsync();
        Task SaveAsync(SavedSession session);
        Task ClearAsync();
        Task<string> GetValueAsync(string key);
        Task SetValueAsync(string key, string value);
        Task RemoveValueAsync(string key);
    }

    public class SavedSession
    {
        public int UserId { get; set; }
        public string Username { get; set; }
        public string FullName { get; set; }
        public string Role { get; set; }
        public int? SchoolId { get; set; }
        public string SchoolName { get; set; }
        public string District { get; set; }
        public string Town { get; set; }
        public string Chiefdom { get; set; }
        public string SchoolResultType { get; set; }
        public DateTime LicenseExpiryDate { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public string PrincipalName { get; set; }
        public DateTime SavedAtUtc { get; set; }

        // ── Teacher-specific fields (new) ──
        // Populated only when Role == "Teacher". Used by TeacherHomePage to
        // decide whether to show the Class Master Roll Call button.
        public bool HasClassMasterRole { get; set; }
        public bool HasSubjectTeacherRole { get; set; }
    }
}