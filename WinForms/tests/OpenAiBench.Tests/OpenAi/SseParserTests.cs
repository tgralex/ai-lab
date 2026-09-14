using System.Text;
using OpenAiBench.Infrastructure.OpenAi;
using Xunit;

namespace OpenAiBench.Tests.OpenAi;

public class SseParserTests
{
    [Fact]
    public async Task ParseAsync_MultipleEvents_YieldsEachWithNameAndData()
    {
        var sse = "event: response.created\n" +
                   "data: {\"type\":\"response.created\"}\n" +
                   "\n" +
                   "event: response.output_text.delta\n" +
                   "data: {\"delta\":\"Hi\"}\n" +
                   "\n";

        var events = await CollectAsync(sse);

        Assert.Equal(2, events.Count);
        Assert.Equal("response.created", events[0].EventName);
        Assert.Equal("{\"type\":\"response.created\"}", events[0].Data);
        Assert.Equal("response.output_text.delta", events[1].EventName);
        Assert.Equal("{\"delta\":\"Hi\"}", events[1].Data);
    }

    [Fact]
    public async Task ParseAsync_NoTrailingBlankLine_StillYieldsFinalEvent()
    {
        var sse = "event: response.completed\ndata: {\"type\":\"response.completed\"}";

        var events = await CollectAsync(sse);

        Assert.Single(events);
        Assert.Equal("response.completed", events[0].EventName);
    }

    private static async Task<List<SseEvent>> CollectAsync(string sse)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sse));
        var events = new List<SseEvent>();
        await foreach (var e in SseParser.ParseAsync(stream))
        {
            events.Add(e);
        }

        return events;
    }
}
