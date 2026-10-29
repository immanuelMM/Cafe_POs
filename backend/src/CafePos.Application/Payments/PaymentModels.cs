namespace CafePos.Application.Payments;

public record PaymentRequest(Guid OrderId, decimal Amount, decimal? AmountTendered);

public record PaymentResult(bool Succeeded, decimal Amount, decimal? AmountTendered, decimal? ChangeGiven, string? ProviderReference, string? FailureReason);
