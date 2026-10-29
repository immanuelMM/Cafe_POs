using CafePos.Application.Common;

namespace CafePos.Application.Payments;

public interface IPaymentProviderResolver
{
    /// <summary>The provider for a payment method key (case-insensitive), or a 400 listing the supported methods.</summary>
    Result<IPaymentProvider> Resolve(string method);
}
