namespace CafePos.Application.Payments;

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
