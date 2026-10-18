namespace CafePos.Api.Services;

/// <summary>
/// Money rules (Philippines, PHP). DECISION: all catalog prices, modifier deltas, line totals and order totals are
/// VAT-INCLUSIVE (tax-inclusive pricing is the norm in PH retail/F&amp;B). VAT is never added on top. For reporting and receipts,
/// the VAT component is extracted from the gross total: vat = total * rate / (100 + rate), rounded to 2 dp (away from zero),
/// and vatable sales = total - vat. The rate is configurable per location (default 12%) and is snapshotted on each order.
/// </summary>
public static class PricingCalculator
{
    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    public static decimal ExtractVat(decimal vatInclusiveTotal, decimal vatRatePercent) =>
        vatRatePercent <= 0 ? 0m : Round(vatInclusiveTotal * vatRatePercent / (100m + vatRatePercent));
}
