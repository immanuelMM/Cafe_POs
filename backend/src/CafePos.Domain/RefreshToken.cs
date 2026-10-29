namespace CafePos.Domain;

public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    /// <summary>Active location the session was created for; null for an Owner's cross-location session.</summary>
    public Guid? LocationId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    /// <summary>Hash of the token that replaced this one when it was rotated (null while active or when revoked another way).</summary>
    public string? ReplacedByTokenHash { get; set; }
    /// <summary>IP address the token was issued to (audit only).</summary>
    public string? CreatedByIp { get; set; }
}
