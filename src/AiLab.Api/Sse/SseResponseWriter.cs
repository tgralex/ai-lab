using System.Text.Json;

namespace AiLab.Api.Sse;

/// <summary>Writes Server-Sent Events frames to an HttpResponse, flushing after each so the browser sees them incrementally.</summary>
public sealed class SseResponseWriter(HttpResponse response)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void PrepareResponse()
    {
        response.Headers.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";
    }

    public async Task WriteAsync(string eventName, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await response.WriteAsync($"event: {eventName}\ndata: {json}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
}
