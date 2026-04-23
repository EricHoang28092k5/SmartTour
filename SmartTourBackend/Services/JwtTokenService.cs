using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace SmartTourBackend.Services;

public class JwtOptions
{
    public string Issuer { get; set; } = "SmartTour";
    public string Audience { get; set; } = "SmartTourClients";
    public string SecretKey { get; set; } = "change-this-super-long-secret-key";
    public int ExpiresMinutes { get; set; } = 120;
}

public interface IJwtTokenService
{
    Task<(string token, DateTime expiresAtUtc)> CreateTokenAsync(IdentityUser user, IList<string> roles);
}

public class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public Task<(string token, DateTime expiresAtUtc)> CreateTokenAsync(IdentityUser user, IList<string> roles)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName ?? user.Email ?? user.Id)
        };

        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SecretKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(_options.ExpiresMinutes);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds);

        var tokenText = new JwtSecurityTokenHandler().WriteToken(token);
        return Task.FromResult((tokenText, expires));
    }
}
