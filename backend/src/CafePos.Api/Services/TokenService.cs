using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CafePos.Api.Data;
using CafePos.Domain;
using CafePos.Infrastructure.Identity;
using CafePos.Api.Infrastructure;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CafePos.Api.Services;

public class JwtOptions
{
    public string Issuer { get; set; } = "CafePos";
    public string Audience { get; set; } = "CafePos.Client";
    /// <summary>Signing key (min 32 chars). Never committed for real environments: supply via Jwt__Key.</summary>
    public string Key { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 30;
    public int RefreshTokenDays { get; set; } = 7;
}

public class TokenService
{
    private readonly JwtOptions _opts;
    private readonly CafeDbContext _db;

    public TokenService(IOptions<JwtOptions> opts, CafeDbContext db)
    {
        _opts = opts.Value;
        _db = db;
    }

    public (string token, DateTime expiresAt) CreateAccessToken(ApplicationUser user, Guid? locationId, string role)
    {
        var expires = DateTime.UtcNow.AddMinutes(_opts.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.Email, user.Email ?? ""),
            new Claim("role", role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        // No location_id for an Owner's cross-location session.
        if (locationId is not null) claims.Add(new Claim(AppClaims.LocationId, locationId.Value.ToString()));
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opts.Key)), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(_opts.Issuer, _opts.Audience, claims, expires: expires, signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(jwt), expires);
    }

    /// <summary>Creates and persists a refresh token; returns the raw value (only its SHA-256 hash is stored).</summary>
    public async Task<(string raw, DateTime expiresAt)> CreateRefreshTokenAsync(Guid userId, Guid? locationId, CancellationToken ct)
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var expires = DateTime.UtcNow.AddDays(_opts.RefreshTokenDays);
        _db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(), UserId = userId, LocationId = locationId, TokenHash = Hash(raw),
            CreatedAt = DateTime.UtcNow, ExpiresAt = expires
        });
        await _db.SaveChangesAsync(ct);
        return (raw, expires);
    }

    public static string Hash(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
