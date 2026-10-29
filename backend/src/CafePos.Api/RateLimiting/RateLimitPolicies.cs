namespace CafePos.Api.RateLimiting;

public static class RateLimitPolicies
{
    public const string Login = "login";
    public const string Refresh = "refresh";
    public const string SwitchLocation = "switch-location";
    public const string TimeclockPunch = "timeclock-punch";
}
