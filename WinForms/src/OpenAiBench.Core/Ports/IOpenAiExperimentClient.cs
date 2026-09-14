using OpenAiBench.Core.Domain;

namespace OpenAiBench.Core.Ports;

public interface IOpenAiExperimentClient
{
    Task<ExecutionRun> ExecuteAsync(
        OpenAiRequestPayload payload,
        IProgress<StreamingUpdate>? progress,
        CancellationToken cancellationToken = default);
}
