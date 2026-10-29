namespace CafePos.Domain;

public class Location
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public string TimeZone { get; set; } = "Asia/Manila";
    public string? ContactPhone { get; set; }
    /// <summary>Stripe Terminal's own Location object id (not this entity); unused until the Stripe phase.</summary>
    public string? StripeTerminalLocationId { get; set; }
    /// <summary>VAT percent (12 = 12%). Catalog prices are VAT-INCLUSIVE; see PricingCalculator.</summary>
    public decimal VatRatePercent { get; set; } = 12m;
    public bool Active { get; set; } = true;
    /// <summary>Per-location receipt counter; incremented atomically when an order completes.</summary>
    public int LastOrderNumber { get; set; }
}
