using CafePos.Application.Common;

namespace CafePos.Application.Payments;

public sealed class PaymentProviderResolver : IPaymentProviderResolver
{
    private readonly Dictionary<string, IPaymentProvider> _providers;

    public PaymentProviderResolver(IEnumerable<IPaymentProvider> providers) =>
        _providers = providers.ToDictionary(p => p.Method, StringComparer.OrdinalIgnoreCase);

    public Result<IPaymentProvider> Resolve(string method) =>
        _providers.TryGetValue(method, out var provider)
            ? Result.Success(provider)
            : Error.BadRequest("Unsupported payment method", $"Payment method '{method}' is not available. Supported: {string.Join(", ", _providers.Keys)}.");
}
