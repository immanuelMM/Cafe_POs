using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace CafePos.Api.Http;

/// <summary>
/// Reverse-proxy trust (configuration section "ForwardedHeaders"). X-Forwarded-For / X-Forwarded-Proto are honored only when they
/// come from a listed proxy; with nothing listed (the default) the middleware is not added at all, because ASP.NET Core treats an
/// empty KnownProxies + KnownNetworks as "trust every sender". Rate limits and the stored client IP therefore use the socket address
/// unless the deployment names its proxy.
/// </summary>
public sealed class ForwardedHeadersSettings
{
    public const string SectionName = "ForwardedHeaders";

    /// <summary>Addresses of trusted proxies, for example ["10.0.0.5"].</summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>Trusted proxy networks in CIDR notation, for example ["10.0.0.0/8"].</summary>
    public string[] KnownNetworks { get; set; } = [];

    public bool HasTrustedProxies => KnownProxies.Length > 0 || KnownNetworks.Length > 0;
}

public sealed class ForwardedHeadersSettingsValidator : IValidateOptions<ForwardedHeadersSettings>
{
    public ValidateOptionsResult Validate(string? name, ForwardedHeadersSettings options)
    {
        var errors = new List<string>();
        foreach (var proxy in options.KnownProxies)
            if (!System.Net.IPAddress.TryParse(proxy, out _)) errors.Add($"ForwardedHeaders:KnownProxies contains an invalid address '{proxy}'.");
        foreach (var network in options.KnownNetworks)
            if (!System.Net.IPNetwork.TryParse(network, out _)) errors.Add($"ForwardedHeaders:KnownNetworks contains an invalid CIDR '{network}'.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

public static class ForwardedHeadersSetup
{
    public static IServiceCollection AddProxyTrust(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<ForwardedHeadersSettings>, ForwardedHeadersSettingsValidator>();
        services.AddOptions<ForwardedHeadersSettings>().BindConfiguration(ForwardedHeadersSettings.SectionName).ValidateOnStart();
        services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<ForwardedHeadersSettings>>((options, settings) =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // The defaults trust loopback proxies; only what the deployment lists is trusted.
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var proxy in settings.Value.KnownProxies) options.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
            foreach (var network in settings.Value.KnownNetworks) options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        });
        return services;
    }
}
