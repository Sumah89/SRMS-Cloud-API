using System;
using System.Text;

namespace SRMS.Desktop.Sync.Grants
{
    /// <summary>
    /// Sierra Leone phone-number normalization.
    ///
    /// Accepts:
    ///   +23299217228
    ///   23299217228
    ///   099217228
    ///   99217228
    ///   099 217 228       (spaces allowed)
    ///   +232 99 217 228
    ///
    /// Always stores as: +232XXXXXXXXX   (E.164, 12 chars, no spaces)
    ///
    /// Rule: strip everything non-digit, strip leading 0, strip a leading
    /// "232" if present, then prepend "+232".
    /// </summary>
    public static class PhoneNumber
    {
        public const string CountryCode = "+232";
        public const int NationalDigits = 8; // SL mobile numbers are 8 digits after +232

        /// <summary>
        /// Normalizes to +232XXXXXXXXX. Returns empty string on failure.
        /// </summary>
        public static string Normalize(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";

            // 1. Keep only digits
            var digits = new StringBuilder(input.Length);
            foreach (char c in input)
                if (char.IsDigit(c)) digits.Append(c);

            if (digits.Length == 0) return "";

            string d = digits.ToString();

            // 2. Strip a leading country code "232" if present
            if (d.StartsWith("232") && d.Length > 3)
                d = d.Substring(3);

            // 3. Strip a leading 0 (national trunk prefix)
            while (d.Length > 0 && d[0] == '0')
                d = d.Substring(1);

            // 4. Must now be exactly NationalDigits long
            if (d.Length != NationalDigits) return "";

            // 5. First digit of an SL mobile number is 2,3,4,5,6,7,8,9 — but
            //    we won't be strict; just sanity-check it's a digit.
            return CountryCode + d;
        }

        /// <summary>
        /// True if <paramref name="normalized"/> is already in +232XXXXXXXXX form.
        /// </summary>
        public static bool IsValid(string normalized)
        {
            if (string.IsNullOrEmpty(normalized)) return false;
            if (!normalized.StartsWith(CountryCode, StringComparison.Ordinal)) return false;
            if (normalized.Length != CountryCode.Length + NationalDigits) return false;
            for (int i = CountryCode.Length; i < normalized.Length; i++)
                if (!char.IsDigit(normalized[i])) return false;
            return true;
        }

        /// <summary>
        /// Human-friendly display: +232 99 217 228
        /// </summary>
        public static string ToDisplay(string normalized)
        {
            if (!IsValid(normalized)) return normalized ?? "";
            string d = normalized.Substring(CountryCode.Length); // 8 digits
            // 2 + 3 + 3 grouping (SL convention)
            return $"{CountryCode} {d.Substring(0, 2)} {d.Substring(2, 3)} {d.Substring(5, 3)}";
        }

        /// <summary>
        /// Strips the +232 prefix — useful for WhatsApp deep links that want
        /// the bare E.164 number without the plus.
        /// </summary>
        public static string ToWhatsAppNumber(string normalized)
        {
            if (!IsValid(normalized)) return "";
            return normalized.Substring(1); // drop the '+'
        }
    }
}