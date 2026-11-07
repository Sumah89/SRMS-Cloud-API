using System;

namespace SRMS.Desktop.Models
{
    public class User
    {
        public int Id { get; set; }
        public int? SchoolId { get; set; }
        public string Username { get; set; }
        public string PasswordHash { get; set; }
        public string FullName { get; set; }
        public string Role { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public bool IsActive { get; set; }
        public DateTime? LastLoginDate { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }

        // For display
        public string SchoolName { get; set; }
    }
}