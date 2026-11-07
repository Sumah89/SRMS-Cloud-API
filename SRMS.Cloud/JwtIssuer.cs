#nullable disable
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace SRMS.Cloud
{
    public sealed class JwtIssuer
    {
        private readonly string _secret;
        private readonly string _issuer;
        private readonly string _audience;

        public JwtIssuer(string secret, string issuer, string audience)
        {
            if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
                throw new InvalidOperationException("JWT secret must be at least 32 characters.");
            _secret = secret;
            _issuer = issuer;
            _audience = audience;
        }

        public string Issue(
            int userId, int schoolId, string role, string fullName,
            int[] childStudentIds = null, int[] teacherClassIds = null)
        {
            var now = DateTime.UtcNow;
            var handler = new JwtSecurityTokenHandler();
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new System.Collections.Generic.List<Claim>
            {
                new Claim("sub", userId.ToString()),
                new Claim("user_id", userId.ToString()),
                new Claim("school_id", schoolId.ToString()),
                new Claim("role", role),
                new Claim("full_name", fullName ?? "")
            };

            if (childStudentIds != null && childStudentIds.Length > 0)
                claims.Add(new Claim("child_ids", string.Join(",", childStudentIds)));

            if (teacherClassIds != null && teacherClassIds.Length > 0)
                claims.Add(new Claim("class_ids", string.Join(",", teacherClassIds)));

            var token = new JwtSecurityToken(
                issuer: _issuer,
                audience: _audience,
                claims: claims,
                notBefore: now,
                expires: now.AddDays(30),
                signingCredentials: creds);

            return handler.WriteToken(token);
        }
    }
}