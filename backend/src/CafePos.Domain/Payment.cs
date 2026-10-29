namespace CafePos.Domain;

public class Payment : ILocationScoped
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid LocationId { get; set; }
    public string Method { get; set; } = "cash";
    public PaymentStatus Status { get; set; }
    public decimal Amount { get; set; }
    public decimal? AmountTendered { get; set; }
    public decimal? ChangeGiven { get; set; }
    public string? ProviderReference { get; set; }
    public DateTime CreatedAt { get; set; }
}
