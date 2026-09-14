using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AiLab.Core.Execution;
using AiLab.Core.Models;
using AiLab.Core.Providers;
using AiLab.Infrastructure.Credentials;
using AiLab.Infrastructure.Sse;

namespace AiLab.Infrastructure.Anthropic;

/// <summary>
/// Anthropic Messages API provider — raw HttpClient + hand-rolled SSE parsing, mirroring
/// OpenAiProvider's approach so timing (TTFT, first-protocol-event) stays precise and
/// provider-comparable.
/// </summary>
public sealed class AnthropicProvider(HttpClient httpClient, ICredentialStore credentialStore) : IAiProvider
{
    private const string ApiVersion = "2023-06-01";

    public string Id => "anthropic";

    public string DisplayName => "Anthropic";

    public async Task<ProviderExecutionResult> ExecuteAsync(
        AiRequestExecutionContext context,
        IProgress<AiStreamEvent>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(AiStreamEvent.Create(AiStreamEventKind.Started));

        var apiKey = await credentialStore.GetApiKeyAsync(Id, cancellationToken);
        if (string.IsNullOrEmpty(apiKey))
        {
            var failure = new FailureInfo { Message = "Anthropic API key is not configured.", StreamingStarted = false };
            progress?.Report(AiStreamEvent.Create(AiStreamEventKind.Error, errorMessage: failure.Message));
            return new ProviderExecutionResult { Success = false, Failure = failure };
        }

        var requestBody = AnthropicRequestBuilder.Build(context);
        var requestJson = requestBody.ToJsonString();
        var normalizedRequestJson = SecretRedactor.Redact(requestJson);

        var retryExecutor = new RetryPolicyExecutor();

        try
        {
            var response = await retryExecutor.ExecuteAsync(async ct =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "messages")
                {
                    Content = new StringContent(requestJson, Encoding.UTF8, "application/json"),
                };
                request.Headers.Add("x-api-key", apiKey);
                request.Headers.Add("anthropic-version", ApiVersion);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

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
                        Message = $"Anthropic returned HTTP {(int)response.StatusCode}.",
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
        var parsed = AnthropicResponseParser.Parse(document.RootElement);

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
        var accumulator = new AnthropicStreamAccumulator();

        await foreach (var sseEvent in SseParser.ParseAsync(stream, ct))
        {
            if (!firstEventSeen)
            {
                firstEventSeen = true;
                progress?.Report(AiStreamEvent.Create(AiStreamEventKind.FirstProtocolEvent));
            }

            if (string.IsNullOrWhiteSpace(sseEvent.Data))
            {
                continue;
            }

            using var eventDoc = JsonDocument.Parse(sseEvent.Data);
            var root = eventDoc.RootElement;
            var type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;

            switch (type)
            {
                case "message_start":
                    if (root.TryGetProperty("message", out var message))
                    {
                        accumulator.ApplyMessageStart(message);
                    }

                    break;

                case "content_block_delta":
                    var textDelta = ExtractDeltaText(root);
                    if (!string.IsNullOrEmpty(textDelta))
                    {
                        accumulator.AppendOutputText(textDelta);
                        progress?.Report(AiStreamEvent.Create(AiStreamEventKind.OutputTextDelta, textDelta: textDelta));
                    }

                    break;

                case "message_delta":
                    accumulator.ApplyMessageDelta(root);
                    break;

                case "error":
                    var errorMessage = root.TryGetProperty("error", out var errorEl) && errorEl.TryGetProperty("message", out var msgEl)
                        ? msgEl.GetString()
                        : "Anthropic returned a stream error.";
                    accumulator.ErrorMessage = errorMessage;
                    break;

                // content_block_start / content_block_stop / message_stop / ping carry no data we need beyond timing.
            }
        }

        if (accumulator.ErrorMessage is not null)
        {
            var failure = new FailureInfo
            {
                Message = accumulator.ErrorMessage,
                StreamingStarted = true,
                RetryCount = retryExecutor.LastRetryCount,
            };
            progress?.Report(AiStreamEvent.Create(AiStreamEventKind.Error, errorMessage: failure.Message));
            return new ProviderExecutionResult { Success = false, Failure = failure, NormalizedRequestJson = normalizedRequestJson };
        }

        progress?.Report(AiStreamEvent.Create(AiStreamEventKind.Completed));

        var parsed = accumulator.ToParsedResponse();
        return BuildResult(parsed, (int)response.StatusCode, accumulator.ToRawJson(), normalizedRequestJson, streamingStarted: true, retryCount: retryExecutor.LastRetryCount);
    }

    private static string? ExtractDeltaText(JsonElement root)
    {
        if (!root.TryGetProperty("delta", out var delta))
        {
            return null;
        }

        var deltaType = delta.TryGetProperty("type", out var t) ? t.GetString() : null;
        return deltaType switch
        {
            "text_delta" when delta.TryGetProperty("text", out var textEl) => textEl.GetString(),
            "input_json_delta" when delta.TryGetProperty("partial_json", out var jsonEl) => jsonEl.GetString(),
            _ => null,
        };
    }

    private static ProviderExecutionResult BuildResult(
        ParsedAnthropicResponse parsed,
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
            FinishReason = parsed.StopReason,
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
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", ApiVersion);

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
