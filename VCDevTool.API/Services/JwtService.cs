using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace VCDevTool.API.Services
{
    public interface IJwtService
    {
        string GenerateToken(string nodeId, string nodeName, IList<string> roles);
        ClaimsPrincipal? ValidateToken(string token);
        string GenerateApiKey(string nodeId);
        bool ValidateApiKey(string apiKey, out string nodeId);
    }

    public class JwtService : IJwtService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<JwtService> _logger;

        public JwtService(IConfiguration configuration, ILogger<JwtService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public string GenerateToken(string nodeId, string nodeName, IList<string> roles)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(GetJwtSecret()));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, nodeId),
                new(ClaimTypes.Name, nodeName),
                new("node_id", nodeId),
                new("node_name", nodeName)
            };

            // Add role claims
            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"] ?? "VCDevTool",
                audience: _configuration["Jwt:Audience"] ?? "VCDevTool",
                claims: claims,
                expires: DateTime.UtcNow.AddHours(24),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public ClaimsPrincipal? ValidateToken(string token)
        {
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.UTF8.GetBytes(GetJwtSecret());

                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = true,
                    ValidIssuer = _configuration["Jwt:Issuer"] ?? "VCDevTool",
                    ValidateAudience = true,
                    ValidAudience = _configuration["Jwt:Audience"] ?? "VCDevTool",
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };

                var principal = tokenHandler.ValidateToken(token, validationParameters, out SecurityToken validatedToken);
                return principal;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Token validation failed for token: {Token}", token.Substring(0, Math.Min(20, token.Length)));
                return null;
            }
        }

        public string GenerateApiKey(string nodeId)
        {
            // Simple API key generation - in production, consider more secure approaches
            var keyData = $"{nodeId}:{DateTime.UtcNow.Ticks}:{Guid.NewGuid()}";
            var keyBytes = Encoding.UTF8.GetBytes(keyData);
            return Convert.ToBase64String(keyBytes);
        }

        public bool ValidateApiKey(string apiKey, out string nodeId)
        {
            nodeId = string.Empty;
            
            try
            {
                var keyBytes = Convert.FromBase64String(apiKey);
                var keyData = Encoding.UTF8.GetString(keyBytes);
                var parts = keyData.Split(':');
                
                if (parts.Length >= 3)
                {
                    nodeId = parts[0];
                    return !string.IsNullOrEmpty(nodeId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "API key validation failed");
            }
            
            return false;
        }

        private string GetJwtSecret()
        {
            var secret = _configuration["Jwt:SecretKey"];
            if (string.IsNullOrEmpty(secret))
            {
                // In production, this should come from secure configuration
                _logger.LogWarning("JWT secret not configured, using default. This is not secure for production!");
                return "VCDevTool-Default-Secret-Key-This-Should-Be-Changed-In-Production-123456789";
            }
            return secret;
        }
    }
} 