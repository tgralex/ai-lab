using OpenAiBench.Core.Domain;

namespace OpenAiBench.Core.Ports;

public interface IModelCapabilityProvider
{
    IReadOnlyList<ModelInfo> GetAll();
    ModelInfo Get(string modelId);
}
