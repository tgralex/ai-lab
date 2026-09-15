using System.Text;
using System.Text.Json;
using AiLab.Core.Execution;
using AiLab.Core.Models;
using AiLab.Core.Providers;
using AiLab.Infrastructure.Credentials;
using AiLab.Infrastructure.Sse;

namespace AiLab.Infrastructure.Gemini;

/// <summary>
/// Google Gemini API provider — raw HttpClient + hand-rolled SSE parsing, mirroring
/// OpenAiProvider/AnthropicProvider/GrokProvider so TTFT / first-protocol-event timing stays
/// precise and provider-comparable. Gemini's REST shape is meaningfully different from all three:
/// auth is a header (not Bearer), and the model id + action ("generateContent" vs
/// "streamGenerateContent") are both part of the URL path rather than the request body.
/// </summary>
public sealed class GeminiProvider(HttpClient httpClient, ICredentialStore credentialStore) : IAiProvider
{
    public string Id => "gemini";

    public string DisplayName => "Gemini";

    public async Task<ProviderExecutionResult> ExecuteAsync(
        AiRequestExecutionContext context,
        IProgress<AiStreamEvent>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(AiStreamEvent.Create(AiStreamEventKind.Started));

        var apiKey = await credentialStore.GetApiKeyAsync(Id, cancellationToken);
        if (string.IsNullOrEmpty(apiKey))
        {
            var failure = new FailureInfo { Message = "Gemini API key is not configured.", StreamingStarted = false };
            progress?.Report(AiStreamEvent.Create(AiStreamEventKind.Error, errorMessage: failure.Message));
            return new ProviderExecutionResult { Success = false, Failure = failure };
        }

        var requestBody = GeminiRequestBuilder.Build(context);
        var requestJson = requestBody.ToJsonString();
        var normalizedRequestJson = SecretRedactor.Redact(requestJson);
        var path = context.Streaming
            ? $"models/{context.ModelId}:streamGenerateContent?alt=sse"
            : $"models/{context.ModelId}:generateContent";

        var retryExecutor = new RetryPolicyExecutor();

        try
        {
            var response = await retryExecutor.ExecuteAsync(async ct =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, path)
                {
                    Content = new StringContent(requestJson, Encoding.UTF8, "application/json"),
                };
                request.Headers.Add("x-goog-api-key", apiKey);

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
                        Message = $"Gemini returned HTTP {(int)response.StatusCode}.",
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
        var parsed = GeminiResponseParser.Parse(document.RootElement);

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
        var accumulator = new GeminiStreamAccumulator();

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
        ParsedGeminiResponse parsed,
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

        var request = new HttpRequestMessage(HttpMethod.Get, "models?pageSize=200");
        request.Headers.Add("x-goog-api-key", apiKey);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(body);

        if (!document.RootElement.TryGetProperty("models", out var modelsElement) || modelsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var models = new List<ProviderModel>();
        foreach (var item in modelsElement.EnumerateArray())
        {
            var name = item.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            // Names come back as "models/gemini-3.5-flash" — strip the prefix for a bare id,
            // matching the other three providers' /models responses.
            var id = name.StartsWith("models/", StringComparison.Ordinal) ? name["models/".Length..] : name;
            if (IsChatCapable(id))
            {
                models.Add(ProviderModel.Unknown(Id, id));
            }
        }

        return models;
    }

    // Google's /v1beta/models list mixes embeddings, TTS, audio/video "live" streaming, image and
    // video generation (Veo), music generation (Lyria), robotics, open-weight Gemma models, and
    // various internal/experimental tooling in alongside actual text-chat models — mirrors
    // OpenAiProvider.IsChatCapable's role, none of these work through this app's generateContent
    // text-chat execution path, so they're excluded outright rather than cluttering the picker
    // with ~50 entries that would just 404 or misbehave if selected.
    private static readonly string[] NonChatMarkers =
    [
        "embedding", "-tts", "audio", "-live", "-image", "veo-", "lyria-", "transcribe",
        "robotics", "computer-use", "gemma", "antigravity", "deep-research", "aqa",
        "nano-banana", "gemini-omni",
    ];

    // Unlike the modality markers above, these are legitimate text-chat model ids by every naming
    // convention — Google's /v1beta/models list still returns them, but confirmed live (real
    // 404s, not a guess) that pro, flash, AND flash-lite of the 2.5 generation are all retired for
    // new API keys: "This model models/gemini-2.5-pro is no longer available to new users. Please
    // update your code to use models/gemini-3.1-pro-preview..." (gemini-2.5-flash points at
    // gemini-3.6-flash, gemini-2.5-flash-lite at gemini-3.5-flash-lite). No field in the /models
    // response signals this account-tier restriction ahead of time, and since every 2.5-tier
    // model checked so far is blocked the same way, exclude the whole generation by prefix rather
    // than denylisting individual ids as each one surfaces its own 404.
    private const string RetiredGenerationPrefix = "gemini-2.5-";

    private static bool IsChatCapable(string modelId)
    {
        var lower = modelId.ToLowerInvariant();
        return !NonChatMarkers.Any(lower.Contains) && !lower.StartsWith(RetiredGenerationPrefix, StringComparison.Ordinal);
    }
}
