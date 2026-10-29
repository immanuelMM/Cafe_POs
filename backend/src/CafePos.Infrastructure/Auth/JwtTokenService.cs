using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CafePos.Application.Common;
using CafePos.Application.Common.Identity;
using CafePos.Application.Features.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CafePos.Infrastructure.Auth;

/// <summary>
/// HMAC-SHA256 JWT access tokens and random, hash-stored refresh tokens. The claim set is part of the contract the frontend and the
/// authorization policies rely on (pinned by ContractTests): sub, nameidentifier, name, emailaddress, role, jti, optional location_id.
/// </summary>
internal sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock) : ITokenService
{
    private readonly JwtOptions _opts = options.Value;

    public AccessToken CreateAccessToken(UserAccount user, Guid? locationId, string role)
    {
        var expires = clock.GetUtcNow().UtcDateTime.AddMinutes(_opts.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Email, user.Email),
            new("role", role),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        // No location_id for an Owner's cross-location session.
        if (locationId is not null) claims.Add(new Claim(AppClaims.LocationId, locationId.Value.ToString()));
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opts.Key)), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(_opts.Issuer, _opts.Audience, claims, expires: expires, signingCredentials: credentials);
        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(jwt), expires);
    }

    public NewRefreshToken CreateRefreshToken()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        return new NewRefreshToken(raw, Hash(raw), clock.GetUtcNow().UtcDateTime.AddDays(_opts.RefreshTokenDays));
    }

    public string Hash(string rawRefreshToken) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawRefreshToken)));
}
