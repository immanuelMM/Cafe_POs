using CafePos.Api.Infrastructure;

namespace CafePos.Api.Payments;

public record PaymentRequest(Guid OrderId, decimal Amount, decimal? AmountTendered);

public record PaymentResult(bool Succeeded, decimal Amount, decimal? AmountTendered, decimal? ChangeGiven, string? ProviderReference, string? FailureReason);

/// <summary>
/// Abstraction over payment processors. Phase 1 ships only Cash. The in-person card/QR provider for the
/// Philippines is undecided (Stripe Terminal is unavailable there), so nothing provider-specific lives outside implementations.
/// </summary>
public interface IPaymentProvider
{
    /// <summary>Stable method key stored on Payment.Method, for example "cash".</summary>
    string Method { get; }
    Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken ct = default);
}

public sealed class CashPaymentProvider : IPaymentProvider
{
    public string Method => "cash";

    public Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken ct = default)
    {
        if (request.AmountTendered is null)
            return Task.FromResult(new PaymentResult(false, request.Amount, null, null, null, "Cash payments require amountTendered."));
        if (request.AmountTendered < request.Amount)
            return Task.FromResult(new PaymentResult(false, request.Amount, request.AmountTendered, null, null,
                $"Amount tendered ({request.AmountTendered:0.00}) is less than the order total ({request.Amount:0.00})."));

        var change = Math.Round(request.AmountTendered.Value - request.Amount, 2, MidpointRounding.AwayFromZero);
        return Task.FromResult(new PaymentResult(true, request.Amount, request.AmountTendered, change, null, null));
    }
}

public interface IPaymentProviderResolver
{
    IPaymentProvider Resolve(string method);
}

public sealed class PaymentProviderResolver : IPaymentProviderResolver
{
    private readonly Dictionary<string, IPaymentProvider> _providers;

    public PaymentProviderResolver(IEnumerable<IPaymentProvider> providers) =>
        _providers = providers.ToDictionary(p => p.Method, StringComparer.OrdinalIgnoreCase);

    public IPaymentProvider Resolve(string method) =>
        _providers.TryGetValue(method, out var p)
            ? p
            : throw ApiException.BadRequest("Unsupported payment method", $"Payment method '{method}' is not available. Supported: {string.Join(", ", _providers.Keys)}.");
}
