using System.Text.Json;
using ElRucio.Platform.Slack;

namespace ElRucio.Platform.Tests;

public class SlackDedupTests
{
    [Fact]
    public void BuildKey_UsesEventsApiEventId()
    {
        using var doc = JsonDocument.Parse("""
            {
              "event_id": "Ev12345"
            }
            """);

        var key = SlackDedupKeyBuilder.Build("events_api", "env-1", doc.RootElement);

        Assert.Equal("events:Ev12345", key);
    }

    [Fact]
    public void BuildKey_UsesSlashTriggerId()
    {
        using var doc = JsonDocument.Parse("""
            {
              "trigger_id": "TrigABC"
            }
            """);

        var key = SlackDedupKeyBuilder.Build("slash_commands", "env-2", doc.RootElement);

        Assert.Equal("slash:TrigABC", key);
    }

    [Fact]
    public void BuildKey_UsesSlashFallbackComposite()
    {
        using var doc = JsonDocument.Parse("""
            {
              "command": "/status",
              "channel_id": "C001",
              "user_id": "U001",
              "text": "now"
            }
            """);

        var key = SlackDedupKeyBuilder.Build("slash_commands", "env-3", doc.RootElement);

        Assert.Equal("slash-fallback:/status:C001:U001:now", key);
    }

    [Fact]
    public void DedupState_SuppressesDuplicateWithinWindow()
    {
        var state = new SlackDedupState(300);

        var first = state.TryBeginProcessing("events:Ev777");
        var second = state.TryBeginProcessing("events:Ev777");

        Assert.True(first);
        Assert.False(second);
    }

    [Fact]
    public void DedupState_FailureUnblocksRetry()
    {
        var state = new SlackDedupState(300);

        Assert.True(state.TryBeginProcessing("slash:Trig777"));
        state.EndProcessingWithFailure("slash:Trig777");

        var retry = state.TryBeginProcessing("slash:Trig777");
        Assert.True(retry);
    }

    [Fact]
    public void BuildKey_UsesEnvelopeFallback_WhenNoSpecificKeys()
    {
        using var doc = JsonDocument.Parse("{}");

        var key = SlackDedupKeyBuilder.Build("events_api", "env-55", doc.RootElement);

        Assert.Equal("envelope:events_api:env-55", key);
    }

    [Fact]
    public void BuildKey_EnvelopeFallbackWithoutEnvelopeId_ProducesUniqueValues()
    {
        using var doc = JsonDocument.Parse("{}");

        var key1 = SlackDedupKeyBuilder.Build("unknown", null, doc.RootElement);
        var key2 = SlackDedupKeyBuilder.Build("unknown", null, doc.RootElement);

        Assert.StartsWith("envelope:unknown:", key1, StringComparison.Ordinal);
        Assert.StartsWith("envelope:unknown:", key2, StringComparison.Ordinal);
        Assert.NotEqual(key1, key2);
    }
}
