using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AiLab.Core.Execution;
using AiLab.Core.Models;
using AiLab.Core.Providers;
using AiLab.Infrastructure.Credentials;
using AiLab.Infrastructure.Sse;

namespace AiLab.Infrastructure.Grok;

/// <summary>
/// xAI (Grok) Chat Completions API provider — raw HttpClient + hand-rolled SSE parsing, mirroring
/// OpenAiProvider/AnthropicProvider so TTFT / first-protocol-event timing stays precise and
/// provider-comparable. xAI's API is OpenAI-Chat-Completions-shaped (`POST /v1/chat/completions`,
/// `choices[0].delta.content` SSE chunks), not the Responses API OpenAiProvider targets, hence its
/// own request/response shapes rather than reusing OpenAiProvider with a different BaseAddress.
/// </summary>
public sealed class GrokProvider(HttpClient httpClient, ICredentialStore credentialStore) : IAiProvider
{
    public string Id => "grok";

    public string DisplayName => "Grok";

    public async Task<ProviderExecutionResult> ExecuteAsync(
        AiRequestExecutionContext context,
        IProgress<AiStreamEvent>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(AiStreamEvent.Create(AiStreamEventKind.Started));

        var apiKey = await credentialStore.GetApiKeyAsync(Id, cancellationToken);
        if (string.IsNullOrEmpty(apiKey))
        {
            var failure = new FailureInfo { Message = "Grok (xAI) API key is not configured.", StreamingStarted = false };
            progress?.Report(AiStreamEvent.Create(AiStreamEventKind.Error, errorMessage: failure.Message));
            return new ProviderExecutionResult { Success = false, Failure = failure };
        }

        var requestBody = GrokRequestBuilder.Build(context);
        var requestJson = requestBody.ToJsonString();
        var normalizedRequestJson = SecretRedactor.Redact(requestJson);

        var retryExecutor = new RetryPolicyExecutor();

        try
        {
            var response = await retryExecutor.ExecuteAsync(async ct =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
                {
                    Content = new StringContent(requestJson, Encoding.UTF8, "application/json"),
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                return await httpClient.SendAsync(
                    request,
                    context.Streaming ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead,
                    ct);
            }, cancellationToken);

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    var failure = new FailureInfo
                    {
                        Message = $"Grok returned HTTP {(int)response.StatusCode}.",
                        HttpStatus = (int)response.StatusCode,
                        ProviderError = SecretRedactor.Redact(errorBody),
                        StreamingStarted = false,
                        RetryCount = retryExecutor.LastRetryCount,
                    };
                    progress?.Report(AiStreamEvent.Create(AiStreamEventKind.Error, errorMessage: failure.Message));
                    return new ProviderExecutionResult
                    {
                        Success = false,
                        HttpStatus = (int)response.StatusCode,
                        Failure = failure,
                        NormalizedRequestJson = normalizedRequestJson,
                    };
                }

                return context.Streaming
                    ? await HandleStreamingResponseAsync(response, normalizedRequestJson, progress, retryExecutor, cancellationToken)
                    : await HandleNonStreamingResponseAsync(response, normalizedRequestJson, progress, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var failure = new FailureInfo
            {
                ExceptionType = ex.GetType().Name,
                Message = SecretRedactor.Redact(ex.Message),
                RetryCount = retryExecutor.LastRetryCount,
            };
            progress?.Report(AiStreamEvent.Create(AiStreamEventKind.Error, errorMessage: failure.Message));
            return new ProviderExecutionResult { Success = false, Failure = failure, NormalizedRequestJson = normalizedRequestJson };
        }
    }

    private static async Task<ProviderExecutionResult> HandleNonStreamingResponseAsync(
        HttpResponseMessage response,
        string normalizedRequestJson,
        IProgress<AiStreamEvent>? progress,
        CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        using var document = JsonDocument.Parse(body);
        var parsed = GrokResponseParser.Parse(document.RootElement);

        // No OutputTextDelta here: a non-streaming call has no observable "first token" moment —
        // see the matching comment in OpenAiProvider for why TTFT stays unset rather than fabricated.
        progress?.Report(AiStreamEvent.Create(AiStreamEventKind.Completed));

        return BuildResult(parsed, (int)response.StatusCode, SecretRedactor.Redact(body), normalizedRequestJson, streamingStarted: false, retryCount: 0);
    }

    private static async Task<ProviderExecutionResult> HandleStreamingResponseAsync(
        HttpResponseMessage response,
        string normalizedRequestJson,
        IProgress<AiStreamEvent>? progress,
        RetryPolicyExecutor retryExecutor,
        CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var firstEventSeen = false;
        var accumulator = new GrokStreamAccumulator();

        await foreach (var sseEvent in SseParser.ParseAsync(stream, ct))
        {
            if (!firstEventSeen)
            {
                firstEventSeen = true;
                progress?.Report(AiStreamEvent.Create(AiStreamEventKind.FirstProtocolEvent));
            }

            if (string.IsNullOrWhiteSpace(sseEvent.Data) || sseEvent.Data == "[DONE]")
            {
                continue;
            }

            using var eventDoc = JsonDocument.Parse(sseEvent.Data);
            accumulator.ApplyChunk(eventDoc.RootElement);

            if (!string.IsNullOrEmpty(accumulator.LastDelta))
            {
                progress?.Report(AiStreamEvent.Create(AiStreamEventKind.OutputTextDelta, textDelta: accumulator.LastDelta));
            }
        }

        progress?.Report(AiStreamEvent.Create(AiStreamEventKind.Completed));

        var parsed = accumulator.ToParsedResponse();
        return BuildResult(parsed, (int)response.StatusCode, accumulator.ToRawJson(), normalizedRequestJson, streamingStarted: true, retryCount: retryExecutor.LastRetryCount);
    }

    private static ProviderExecutionResult BuildResult(
        ParsedGrokResponse parsed,
        int httpStatus,
        string rawResponseJson,
        string normalizedRequestJson,
        bool streamingStarted,
        int retryCount)
    {
        if (parsed.ErrorMessage is not null)
        {
            return new ProviderExecutionResult
            {
                Success = false,
                RawResponseJson = rawResponseJson,
                NormalizedRequestJson = normalizedRequestJson,
                HttpStatus = httpStatus,
                Usage = parsed.Usage,
                Failure = new FailureInfo { Message = parsed.ErrorMessage, HttpStatus = httpStatus, StreamingStarted = streamingStarted, RetryCount = retryCount },
            };
        }

        return new ProviderExecutionResult
        {
            Success = true,
            OutputText = parsed.OutputText,
            RawResponseJson = rawResponseJson,
            NormalizedRequestJson = normalizedRequestJson,
            ResponseId = parsed.ResponseId,
            ActualModel = parsed.ActualModel,
            FinishReason = parsed.FinishReason,
            HttpStatus = httpStatus,
            Usage = parsed.Usage,
        };
    }

    public async Task<IReadOnlyList<ProviderModel>> GetModelsAsync(CancellationToken cancellationToken)
    {
        var apiKey = await credentialStore.GetApiKeyAsync(Id, cancellationToken);
        if (string.IsNullOrEmpty(apiKey))
        {
            return [];
        }

        var request = new HttpRequestMessage(HttpMethod.Get, "models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(body);

        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var models = new List<ProviderModel>();
        foreach (var item in data.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            if (!string.IsNullOrEmpty(id))
            {
                models.Add(ProviderModel.Unknown(Id, id));
            }
        }

        return models;
    }
}
