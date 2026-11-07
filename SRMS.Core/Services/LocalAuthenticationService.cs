using System;
using SRMS.Desktop.Data;
using SRMS.Desktop.Models;

namespace SRMS.Desktop.Services
{
    public sealed class LocalAuthenticationService
    {
        private readonly DatabaseHelper _database;

        public LocalAuthenticationService(DatabaseHelper database)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
        }

        public User Authenticate(string username, string password, string schoolName, string district, string town)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
                return null;

            return _database.AuthenticateUser(
                username.Trim(),
                password,
                schoolName?.Trim() ?? string.Empty,
                district?.Trim() ?? string.Empty,
                town?.Trim() ?? string.Empty);
        }
    }
}
