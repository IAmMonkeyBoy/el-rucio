using ElRucio.Shared.Models;
using ElRucio.Shared.Options;

namespace ElRucio.Platform.Tests;

public class SlackDefaultConfigurationTests
{
    [Fact]
    public void PlatformOptions_DefaultProvider_IsSlack()
    {
        var options = new PlatformOptions();

        Assert.Equal("slack", options.Provider);
    }

    [Fact]
    public void InboundMessage_DefaultProvider_IsSlack()
    {
        var message = new InboundMessage(
            ChatId: "chat-1",
            Text: "hello",
            Attachments: [],
            ReceivedUtc: DateTimeOffset.UtcNow);

        Assert.Equal("slack", message.Provider);
    }

    [Fact]
    public void OutboundMessage_DefaultProvider_IsSlack()
    {
        var message = new OutboundMessage(
            ChatId: "chat-1",
            Text: "hello");

        Assert.Equal("slack", message.Provider);
    }

    [Fact]
    public void ScheduledTaskItem_DefaultProvider_IsSlack()
    {
        var item = new ScheduledTaskItem(
            Id: "task-1",
            ChatId: "chat-1",
            SessionId: "session-1",
            Cron: "0 * * * *",
            Prompt: "ping",
            Enabled: true,
            NextRunUtc: DateTimeOffset.UtcNow,
            LastRunUtc: null,
            CreatedUtc: DateTimeOffset.UtcNow);

        Assert.Equal("slack", item.Provider);
    }
}
