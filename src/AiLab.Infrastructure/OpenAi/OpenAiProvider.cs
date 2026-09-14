using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AiLab.Core.Models;
using AiLab.Core.Providers;
using AiLab.Infrastructure.Credentials;
using AiLab.Infrastructure.Sse;

namespace AiLab.Infrastructure.OpenAi;

/// <summary>
/// OpenAI Responses API provider — raw HttpClient + hand-rolled SSE parsing (ported from the
/// WinForms OpenAiResponsesClient), so TTFT / first-protocol-event timing stays precise and
/// provider-comparable rather than however an SDK happens to buffer its stream.
/// </summary>
public sealed class OpenAiProvider(HttpClient httpClient, ICredentialStore credentialStore) : IAiProvider
{
    public string Id => "openai";

    public string DisplayName => "OpenAI";

    public async Task<ProviderExecutionResult> ExecuteAsync(
        AiRequestExecutionContext context,
        IProgress<AiStreamEvent>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(AiStreamEvent.Create(AiStreamEventKind.Started));

        var apiKey = await credentialStore.GetApiKeyAsync(Id, cancellationToken);
        if (string.IsNullOrEmpty(apiKey))
        {
            var failure = new Core.Execution.FailureInfo { Message = "OpenAI API key is not configured.", StreamingStarted = false };
            progress?.Report(AiStreamEvent.Create(AiStreamEventKind.Error, errorMessage: failure.Message));
            return new ProviderExecutionResult { Success = false, Failure = failure };
        }

        var requestBody = OpenAiRequestBuilder.Build(context);
        var requestJson = requestBody.ToJsonString();
        var normalizedRequestJson = SecretRedactor.Redact(requestJson);

        var retryExecutor = new RetryPolicyExecutor();
        var streamingStarted = false;

        try
        {
            var response = await retryExecutor.ExecuteAsync(async ct =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "responses")
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
                    var failure = new Core.Execution.FailureInfo
                    {
                        Message = $"OpenAI returned HTTP {(int)response.StatusCode}.",
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
            var failure = new Core.Execution.FailureInfo
            {
                ExceptionType = ex.GetType().Name,
                Message = SecretRedactor.Redact(ex.Message),
                StreamingStarted = streamingStarted,
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
        var parsed = OpenAiResponseParser.Parse(document.RootElement);

        // No OutputTextDelta here: a non-streaming call has no observable "first token" moment —
        // the whole response arrives at once, so TTFT/generation-duration/tokens-per-sec correctly
        // stay unset (not a fabricated sub-millisecond figure) rather than reported as if measured.
        progress?.Report(AiStreamEvent.Create(AiStreamEventKind.Completed));

        return BuildResult(parsed, response, SecretRedactor.Redact(body), normalizedRequestJson, streamingStarted: false, retryCount: 0);
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
        string? finalPayload = null;
        ParsedOpenAiResponse? finalParsed = null;

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
            var type = eventDoc.RootElement.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;

            switch (type)
            {
                case "response.output_text.delta":
                    var delta = eventDoc.RootElement.TryGetProperty("delta", out var deltaElement) ? deltaElement.GetString() : null;
                    if (!string.IsNullOrEmpty(delta))
                    {
                        progress?.Report(AiStreamEvent.Create(AiStreamEventKind.OutputTextDelta, textDelta: delta));
                    }

                    break;

                case "response.completed":
                case "response.incomplete":
                case "response.failed":
                    if (eventDoc.RootElement.TryGetProperty("response", out var responseElement))
                    {
                        finalPayload = responseElement.GetRawText();
                        finalParsed = OpenAiResponseParser.Parse(responseElement);
                    }

                    break;
            }
        }

        if (finalParsed is null)
        {
            var failure = new Core.Execution.FailureInfo
            {
                Message = "Stream ended without a terminal response.completed/failed event.",
                StreamingStarted = true,
                RetryCount = retryExecutor.LastRetryCount,
            };
            progress?.Report(AiStreamEvent.Create(AiStreamEventKind.Error, errorMessage: failure.Message));
            return new ProviderExecutionResult { Success = false, Failure = failure, NormalizedRequestJson = normalizedRequestJson };
        }

        progress?.Report(AiStreamEvent.Create(AiStreamEventKind.Completed));

        return BuildResult(finalParsed, response, SecretRedactor.Redact(finalPayload ?? string.Empty), normalizedRequestJson, streamingStarted: true, retryCount: retryExecutor.LastRetryCount);
    }

    private static ProviderExecutionResult BuildResult(
        ParsedOpenAiResponse parsed,
        HttpResponseMessage response,
        string rawResponseJson,
        string normalizedRequestJson,
        bool streamingStarted,
        int retryCount)
    {
        if (parsed.ErrorMessage is not null || parsed.Status is "failed" or "incomplete")
        {
            var failure = new Core.Execution.FailureInfo
            {
                Message = parsed.ErrorMessage ?? $"Response ended with status '{parsed.Status}'.",
                HttpStatus = (int)response.StatusCode,
                StreamingStarted = streamingStarted,
                RetryCount = retryCount,
            };

            return new ProviderExecutionResult
            {
                Success = false,
                RawResponseJson = rawResponseJson,
                NormalizedRequestJson = normalizedRequestJson,
                ResponseId = parsed.ResponseId,
                ActualModel = parsed.ActualModel,
                FinishReason = parsed.Status,
                HttpStatus = (int)response.StatusCode,
                Usage = parsed.Usage,
                Failure = failure,
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
            FinishReason = parsed.Status,
            HttpStatus = (int)response.StatusCode,
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
