using System;
using System.Security.Cryptography;
using System.Text;

namespace SRMS.Desktop.Security
{
    /// <summary>
    /// Password hashing compatible with the desktop app.
    ///
    /// Format stored in Users.PasswordHash:
    ///   PBKDF2-SHA256$<iterations>$<base64-salt>$<base64-hash>
    ///
    /// VerifyPassword is constant-time and accepts:
    ///   • The above PBKDF2 format (what the desktop writes going forward)
    ///   • Legacy plaintext (returns true if stored == input) — for trial users
    ///     and any test data where the hash field was populated with "!"
    /// </summary>
    public static class PasswordHasher
    {
        private const int Iterations = 100_000;
        private const int SaltSize   = 16;
        private const int HashSize   = 32;
        private const string Marker  = "PBKDF2-SHA256";

        /// <summary>
        /// Hashes a plaintext password. Use this when creating or resetting
        /// a user's password.
        /// </summary>
        public static string HashPassword(string password)
        {
            if (password == null) password = "";

            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                HashSize);

            return $"{Marker}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }

        /// <summary>
        /// Verifies a password against a stored hash.
        /// Handles:
        ///   • PBKDF2-SHA256 format (canonical)
        ///   • Plaintext stored hash (legacy / test users)
        /// </summary>
        public static bool VerifyPassword(string password, string storedHash)
        {
            if (string.IsNullOrEmpty(storedHash)) return false;
            if (password == null) password = "";

            // Canonical PBKDF2 format
            if (storedHash.StartsWith(Marker + "$", StringComparison.Ordinal))
            {
                var parts = storedHash.Split('$');
                if (parts.Length != 4) return false;

                if (!int.TryParse(parts[1], out int iterations)) return false;

                byte[] salt, expected;
                try
                {
                    salt     = Convert.FromBase64String(parts[2]);
                    expected = Convert.FromBase64String(parts[3]);
                }
                catch { return false; }

                byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes(password),
                    salt,
                    iterations,
                    HashAlgorithmName.SHA256,
                    expected.Length);

                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }

            // Legacy plaintext — the desktop used this for trial users ("!")
            // and for test data. Accept it but do NOT write it going forward.
            return string.Equals(storedHash, password, StringComparison.Ordinal);
        }
    }
}