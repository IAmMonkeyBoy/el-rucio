using ElRucio.Platform.Slack;

namespace ElRucio.Platform.Tests;

public class SlackSocketEnvelopeParserTests
{
    [Fact]
    public void Parse_WhitespacePayload_ReturnsNull()
    {
        var parsed = SlackSocketEnvelopeParser.Parse("   ");

        Assert.Null(parsed);
    }

    [Fact]
    public void Parse_NonRelevantChannelMessage_IsIgnored()
    {
        var payload = """
            {
              "envelope_id": "env-edge-1",
              "type": "events_api",
              "payload": {
                "event_id": "EvEdge1",
                "event": {
                  "type": "message",
                  "channel_type": "channel",
                  "channel": "C333",
                  "user": "U333",
                  "text": "hello in channel"
                }
              }
            }
            """;

        var parsed = SlackSocketEnvelopeParser.Parse(payload);

        Assert.NotNull(parsed);
        Assert.Equal(SlackSocketActionType.None, parsed!.ActionType);
    }

    [Fact]
    public void Parse_SlashCommandMissingFields_ReturnsNoneAction()
    {
        var payload = """
            {
              "envelope_id": "env-edge-2",
              "type": "slash_commands",
              "payload": {
                "command": "/status"
              }
            }
            """;

        var parsed = SlackSocketEnvelopeParser.Parse(payload);

        Assert.NotNull(parsed);
        Assert.Equal(SlackSocketActionType.None, parsed!.ActionType);
    }

    [Fact]
    public void Parse_UnknownEnvelopeType_ReturnsNoneActionWithFallbackKey()
    {
        var payload = """
            {
              "envelope_id": "env-edge-3",
              "type": "hello"
            }
            """;

        var parsed = SlackSocketEnvelopeParser.Parse(payload);

        Assert.NotNull(parsed);
        Assert.Equal(SlackSocketActionType.None, parsed!.ActionType);
        Assert.Equal("envelope:hello:env-edge-3", parsed.DedupKey);
    }

    [Fact]
    public void Parse_MalformedJson_ThrowsJsonException()
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => SlackSocketEnvelopeParser.Parse("{ this is not json }"));
    }
}
