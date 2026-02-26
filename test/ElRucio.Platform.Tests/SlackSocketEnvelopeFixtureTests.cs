using System.Text.Json;
using ElRucio.Platform.Slack;

namespace ElRucio.Platform.Tests;

public class SlackSocketEnvelopeFixtureTests
{
    [Theory]
    [MemberData(nameof(GetFixtureCases))]
    public void Parse_FixtureCases_ExpectedAction(SlackEnvelopeFixture fixture)
    {
        var payloadJson = JsonSerializer.Serialize(fixture.Payload);
        var parsed = SlackSocketEnvelopeParser.Parse(payloadJson);

        Assert.NotNull(parsed);
        Assert.Equal(Enum.Parse<SlackSocketActionType>(fixture.Expect.ActionType), parsed!.ActionType);
        Assert.Equal(fixture.Expect.DedupKey, parsed.DedupKey);

        if (parsed.ActionType == SlackSocketActionType.InboundMessage)
        {
            Assert.NotNull(parsed.Inbound);
            Assert.Equal(fixture.Expect.ChannelId, parsed.Inbound!.ChannelId);
            Assert.Equal(fixture.Expect.UserId, parsed.Inbound.UserId);
            Assert.Equal(fixture.Expect.ThreadTs, parsed.Inbound.ThreadTs);
            Assert.Equal(fixture.Expect.Text, parsed.Inbound.Text);
            Assert.Equal(fixture.Expect.FileCount, parsed.Inbound.Files.Count);
            Assert.Null(parsed.Slash);
            return;
        }

        if (parsed.ActionType == SlackSocketActionType.SlashCommand)
        {
            Assert.NotNull(parsed.Slash);
            Assert.Equal(fixture.Expect.ChannelId, parsed.Slash!.ChannelId);
            Assert.Equal(fixture.Expect.UserId, parsed.Slash.UserId);
            Assert.Equal(fixture.Expect.SlashCommand, parsed.Slash.Command);
            Assert.Equal(fixture.Expect.SlashText, parsed.Slash.Text);
            Assert.Null(parsed.Inbound);
            return;
        }

        Assert.Null(parsed.Inbound);
        Assert.Null(parsed.Slash);
    }

    public static IEnumerable<object[]> GetFixtureCases()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "SlackEnvelope");
        foreach (var file in Directory.GetFiles(root, "*.json", SearchOption.TopDirectoryOnly).OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            var json = File.ReadAllText(file);
            var fixture = JsonSerializer.Deserialize<SlackEnvelopeFixture>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (fixture is not null)
            {
                yield return [fixture];
            }
        }
    }

    public sealed record SlackEnvelopeFixture(string Name, JsonElement Payload, SlackEnvelopeExpected Expect);

    public sealed record SlackEnvelopeExpected(
        string ActionType,
        string DedupKey,
        string? ChannelId,
        string? UserId,
        string? ThreadTs,
        string? Text,
        int FileCount,
        string? SlashCommand,
        string? SlashText);
}
