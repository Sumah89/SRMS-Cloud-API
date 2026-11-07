using System;
using System.Linq;
using System.Text;

namespace SRMS.Core.Sync.Grants
{
    /// <summary>
    /// Generates 12-character access codes with a 4-character school prefix.
    ///
    /// Prefix rule:
    ///   - First 2 letters of the school name
    ///   - Plus the first letter of the district
    ///   - Plus the first letter of the town/location
    ///   - All uppercased, padded with 'X' if any part is missing
    ///
    /// Examples:
    ///   Kolenten Junior Secondary School + Kambia + Salatoke → KJKS
    ///   Sierra Leone Muslim Brotherhood  + Kambia + Kambia   → SLKK
    ///   Government Technical Secondary School + Kambia + Kambia → GTKK
    ///   A.I.S. Secondary School + Kambia + Salatoke → AISK
    /// </summary>
    public static class CodeGenerator
    {
        // No I, O, 0, 1 — prevents confusion when reading codes off WhatsApp
        private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        public static string GeneratePrefix(string schoolName, string district, string town)
        {
            string namePrefix = ExtractNamePrefix2Letters(schoolName);
            string districtPrefix = ExtractFirstLetter(district);
            string townPrefix = ExtractFirstLetter(town);
            return (namePrefix + districtPrefix + townPrefix).ToUpperInvariant();
        }

        private static string ExtractNamePrefix2Letters(string schoolName)
        {
            if (string.IsNullOrWhiteSpace(schoolName)) return "XX";

            var words = schoolName.Split(
                new[] { ' ', '\t', '-', ',', '.', '/', '\\', '(', ')' },
                StringSplitOptions.RemoveEmptyEntries);

            var sb = new StringBuilder(2);
            foreach (var word in words)
            {
                if (sb.Length >= 2) break;
                char c = word.FirstOrDefault(char.IsLetter);
                if (c != '\0') sb.Append(char.ToUpperInvariant(c));
            }

            while (sb.Length < 2) sb.Append('X');
            return sb.ToString();
        }

        private static string ExtractFirstLetter(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "X";
            char c = value.FirstOrDefault(char.IsLetter);
            return c == '\0' ? "X" : char.ToUpperInvariant(c).ToString();
        }

        /// <summary>
        /// Generates a 12-character code: 4-char school prefix + 8 random chars.
        /// </summary>
        public static string GenerateCode(string schoolName, string district, string town, Random rng = null)
        {
            string prefix = GeneratePrefix(schoolName, district, town);
            rng ??= new Random();

            var sb = new StringBuilder(12);
            sb.Append(prefix);
            for (int i = 0; i < 8; i++)
                sb.Append(Alphabet[rng.Next(Alphabet.Length)]);
            return sb.ToString();
        }

        /// <summary>
        /// True if the code is exactly 12 chars from the safe alphabet.
        /// </summary>
        public static bool IsWellFormed(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length != 12) return false;
            foreach (char c in code)
                if (Alphabet.IndexOf(c) < 0) return false;
            return true;
        }
    }
}