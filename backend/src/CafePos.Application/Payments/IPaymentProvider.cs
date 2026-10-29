namespace CafePos.Application.Payments;

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
