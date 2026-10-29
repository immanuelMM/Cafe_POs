using CafePos.Application.Common;
using CafePos.Application.Payments;

namespace CafePos.Application.Tests;

public class PaymentProviderTests
{
    [Fact]
    public async Task Cash_computes_change_and_rejects_short_or_missing_tender()
    {
        var cash = new CashPaymentProvider();
        var ok = await cash.ChargeAsync(new PaymentRequest(Guid.NewGuid(), 150m, 200m));
        Assert.True(ok.Succeeded);
        Assert.Equal(50m, ok.ChangeGiven);

        var short1 = await cash.ChargeAsync(new PaymentRequest(Guid.NewGuid(), 150m, 100m));
        Assert.False(short1.Succeeded);
        Assert.Contains("less than the order total", short1.FailureReason);
        Assert.False((await cash.ChargeAsync(new PaymentRequest(Guid.NewGuid(), 150m, null))).Succeeded);
    }

    [Fact]
    public void The_resolver_is_case_insensitive_and_rejects_unknown_methods()
    {
        var resolver = new PaymentProviderResolver([new CashPaymentProvider()]);

        Assert.Equal("cash", resolver.Resolve("CASH").Value.Method);
        resolver.Resolve("stripe").ShouldFailWith(ErrorKind.BadRequest, "Unsupported payment method");
    }
}
