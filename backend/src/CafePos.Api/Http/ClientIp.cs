namespace CafePos.Api.Http;

/// <summary>The caller's address as text, safe to store and to use as a rate-limit key.</summary>
public static class ClientIp
{
    /// <summary>Longest address stored in RefreshTokens.CreatedByIp (a full IPv4-mapped IPv6 address is 45 characters).</summary>
    public const int MaxLength = 45;

    /// <summary>The remote address without an IPv6 zone id ("fe80::1%eth0" becomes "fe80::1"), capped at <see cref="MaxLength"/>; null when unknown.</summary>
    public static string? Of(HttpContext http)
    {
        var text = http.Connection.RemoteIpAddress?.ToString();
        if (text is null) return null;
        var zone = text.IndexOf('%', StringComparison.Ordinal);
        if (zone >= 0) text = text[..zone];
        return text.Length <= MaxLength ? text : text[..MaxLength];
    }
}
