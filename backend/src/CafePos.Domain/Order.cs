namespace CafePos.Domain;

public class Order : ILocationScoped
{
    /// <summary>Client-generated GUID.</summary>
    public Guid Id { get; set; }
    public Guid LocationId { get; set; }
    /// <summary>Per-location receipt number, assigned on completion.</summary>
    public int? OrderNumber { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Open;
    public Guid CashierId { get; set; }
    public string CashierName { get; set; } = "";
    public string? Note { get; set; }
    public decimal VatRatePercent { get; set; }
    /// <summary>VAT-inclusive total (sum of line totals).</summary>
    public decimal Total { get; set; }
    /// <summary>VAT portion contained within Total.</summary>
    public decimal VatAmount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CanceledAt { get; set; }
    public List<OrderItem> Items { get; set; } = new();
    public List<Payment> Payments { get; set; } = new();
}
