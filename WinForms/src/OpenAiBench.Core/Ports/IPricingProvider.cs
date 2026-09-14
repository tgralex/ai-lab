using OpenAiBench.Core.Domain;

namespace OpenAiBench.Core.Ports;

public interface IPricingProvider
{
    PricingEntry? TryGet(string modelId);
}
