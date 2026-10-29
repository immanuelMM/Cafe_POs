using CafePos.Domain;

namespace CafePos.Domain.Tests;

public class PricingCalculatorTests
{
    [Theory]
    [InlineData(112, 12, 12.00)]
    [InlineData(150, 12, 16.07)]   // 150 * 12 / 112 = 16.0714...
    [InlineData(95, 12, 10.18)]    // 10.178... rounds up
    [InlineData(100, 0, 0)]
    [InlineData(100, -5, 0)]
    public void Vat_is_extracted_from_a_vat_inclusive_total(decimal total, decimal rate, decimal expected) =>
        Assert.Equal(expected, PricingCalculator.ExtractVat(total, rate));

    [Theory]
    [InlineData(1.005, 1.01)]   // away from zero, not banker's rounding
    [InlineData(2.5, 2.5)]
    [InlineData(0.004, 0.00)]
    [InlineData(-1.005, -1.01)]
    public void Amounts_round_to_two_places_away_from_zero(decimal amount, decimal expected) =>
        Assert.Equal(expected, PricingCalculator.Round(amount));

    [Fact]
    public void Vat_plus_vatable_sales_equals_the_total()
    {
        const decimal total = 337.55m;
        var vat = PricingCalculator.ExtractVat(total, 12m);

        Assert.Equal(total, vat + (total - vat));
        Assert.Equal(36.17m, vat);
    }
}
