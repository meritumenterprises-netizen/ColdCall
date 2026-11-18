using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ColdCall.Services;

public static class McpSessionToken
{
    public const string Issuer = "ColdCall";
    public const string Audience = "ColdCall.McpServer";
    public static SymmetricSecurityKey Key(IConfiguration configuration)
    {
        var key = configuration["Mcp:SigningKey"];
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32)
            throw new InvalidOperationException("Skonfiguruj Mcp:SigningKey (co najmniej 32 bajty) w obu aplikacjach.");
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
    }
    public static string Create(int userId, IConfiguration configuration) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(Issuer, Audience,
            [new Claim("sub", userId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
             new Claim("scope", "companies:read"), new Claim("jti", Guid.NewGuid().ToString("N"))],
            notBefore: DateTime.UtcNow.AddSeconds(-5), expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(Key(configuration), SecurityAlgorithms.HmacSha256)));
}
