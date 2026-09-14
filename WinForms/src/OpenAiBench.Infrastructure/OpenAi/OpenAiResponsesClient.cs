using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenAiBench.Core.Domain;
using OpenAiBench.Core.Ports;

namespace OpenAiBench.Infrastructure.OpenAi;

public sealed class OpenAiResponsesClient : IOpenAiExperimentClient
{
    private readonly HttpClient _httpClient;
    private readonly IClock _clock;
    private readonly IModelCapabilityProvider _modelCapabilityProvider;
    private readonly RetryPolicyExecutor _retryPolicy;
    private readonly ILogger<OpenAiResponsesClient>? _logger;

    public OpenAiResponsesClient(
        HttpClient httpClient,
        IClock clock,
        IModelCapabilityProvider modelCapabilityProvider,
        RetryPolicyExecutor? retryPolicy = null,
        ILogger<OpenAiResponsesClient>? logger = null)
    {
        _httpClient = httpClient;
        _clock = clock;
        _modelCapabilityProvider = modelCapabilityProvider;
        _retryPolicy = retryPolicy ?? new RetryPolicyExecutor();
        _logger = logger;
    }

    public async Task<ExecutionRun> ExecuteAsync(
        OpenAiRequestPayload payload,
        IProgress<StreamingUpdate>? progress,
        CancellationToken cancellationToken = default)
    {
        var modelInfo = _modelCapabilityProvider.Get(payload.Model);
        var requestBody = ResponsesApiRequestBuilder.Build(payload, modelInfo);
        var requestJson = requestBody.ToJsonString();

        var run = new ExecutionRun
        {
            Snapshot = payload.Snapshot,
            RequestedModel = payload.Model,
            RawRequest = SecretRedactor.Redact(requestJson),
            RequestByteSize = Encoding.UTF8.GetByteCount(requestJson)
        };

        var startedAt = _clock.UtcNow;
        run.StartedAt = startedAt;
        run.Status = ExecutionStatus.Running;
        progress?.Report(new StreamingUpdate { Status = ExecutionStatus.Running });

        var timing = new StreamingTimingTracker();
        var outputBuilder = new StringBuilder();
        var retryDelays = new List<TimeSpan>();

        try
        {
            var response = await SendWithRetryAsync(requestJson, run, retryDelays, cancellationToken).ConfigureAwait(false);
            if (response is null)
            {
                run.Status = ExecutionStatus.Failed;
                run.Error = $"HTTP {run.HttpStatus}";
            }
            else
            {
                using (response)
                {
                    if (payload.Stream)
                    {
                        await ProcessStreamingBodyAsync(response, run, timing, outputBuilder, progress, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await ProcessNonStreamingBodyAsync(response, run, cancellationToken).ConfigureAwait(false);
                    }
                }

                run.Status = ExecutionStatus.Completed;
            }
        }
        catch (OperationCanceledException)
        {
            run.Status = ExecutionStatus.Canceled;
            run.FailedAfterStreamingBegan = timing.FirstResponseEventAt.HasValue;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "OpenAI request failed for model {Model}", payload.Model);
            run.Status = ExecutionStatus.Failed;
            run.Error = ex.Message;
            run.ExceptionType = ex.GetType().Name;
            run.FailedAfterStreamingBegan = timing.FirstResponseEventAt.HasValue;
        }

        run.RetryCount = retryDelays.Count;
        run.RetryDelays = retryDelays;
        FinalizeTiming(run, timing, startedAt, outputBuilder.ToString());
        return run;
    }

    private async Task<HttpResponseMessage?> SendWithRetryAsync(
        string requestJson,
        ExecutionRun run,
        List<TimeSpan> retryDelays,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            HttpResponseMessage response;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "responses")
                {
                    Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
                };
                response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (attempt < _retryPolicy.MaxAttempts - 1)
            {
                var delay = _retryPolicy.GetDelay(attempt);
                retryDelays.Add(delay);
                attempt++;
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            run.HttpStatus = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            if (RetryPolicyExecutor.IsTransientStatus(response.StatusCode) && attempt < _retryPolicy.MaxAttempts - 1)
            {
                response.Dispose();
                var delay = _retryPolicy.GetDelay(attempt);
                retryDelays.Add(delay);
                attempt++;
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            run.ApiErrorBody = SecretRedactor.Redact(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            response.Dispose();
            return null;
        }
    }

    private async Task ProcessNonStreamingBodyAsync(HttpResponseMessage response, ExecutionRun run, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        run.ResponseByteSize = Encoding.UTF8.GetByteCount(body);
        run.RawResponse = SecretRedactor.Redact(body);

        using var document = JsonDocument.Parse(body);
        ResponsesApiResponseParser.ApplyResponseJson(run, document.RootElement);
    }

    private async Task ProcessStreamingBodyAsync(
        HttpResponseMessage response,
        ExecutionRun run,
        StreamingTimingTracker timing,
        StringBuilder outputBuilder,
        IProgress<StreamingUpdate>? progress,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        long responseBytes = 0;

        await foreach (var sseEvent in SseParser.ParseAsync(stream, cancellationToken).ConfigureAwait(false))
        {
            if (string.IsNullOrEmpty(sseEvent.Data) || sseEvent.Data == "[DONE]")
            {
                continue;
            }

            responseBytes += Encoding.UTF8.GetByteCount(sseEvent.Data);
            var now = _clock.UtcNow;

            using var document = TryParse(sseEvent.Data);
            if (document is null)
            {
                continue;
            }

            var root = document.RootElement;
            var eventType = sseEvent.EventName ?? ResponsesApiResponseParser.GetString(root, "type");

            switch (eventType)
            {
                case "response.output_text.delta":
                    var delta = ResponsesApiResponseParser.GetString(root, "delta");
                    timing.OnOutputTextDelta(now, delta);
                    if (!string.IsNullOrEmpty(delta))
                    {
                        outputBuilder.Append(delta);
                        run.Status = ExecutionStatus.Streaming;
                        progress?.Report(new StreamingUpdate { Status = ExecutionStatus.Streaming, DeltaText = delta });
                    }
                    break;

                case "response.completed":
                case "response.incomplete":
                case "response.failed":
                    timing.OnEvent(now);
                    if (root.TryGetProperty("response", out var responseElement))
                    {
                        ResponsesApiResponseParser.ApplyResponseJson(run, responseElement);
                        run.RawResponse = SecretRedactor.Redact(responseElement.GetRawText());
                    }
                    break;

                default:
                    timing.OnEvent(now);
                    break;
            }
        }

        run.ResponseByteSize = responseBytes;
        if (string.IsNullOrEmpty(run.Output))
        {
            run.Output = outputBuilder.ToString();
        }
    }

    private static JsonDocument? TryParse(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void FinalizeTiming(ExecutionRun run, StreamingTimingTracker timing, DateTimeOffset startedAt, string fallbackOutput)
    {
        var finishedAt = _clock.UtcNow;
        run.FinishedAt = finishedAt;
        run.TotalDuration = finishedAt - startedAt;
        run.FirstResponseEventAt = timing.FirstResponseEventAt;
        run.FirstTokenAt = timing.FirstTokenAt;
        run.TimeToFirstResponseEvent = timing.FirstResponseEventAt.HasValue ? timing.FirstResponseEventAt.Value - startedAt : null;
        run.TimeToFirstToken = timing.FirstTokenAt.HasValue ? timing.FirstTokenAt.Value - startedAt : null;
        run.GenerationDuration = timing.FirstTokenAt.HasValue ? finishedAt - timing.FirstTokenAt.Value : null;

        if (string.IsNullOrEmpty(run.Output))
        {
            run.Output = fallbackOutput;
        }
    }
}
