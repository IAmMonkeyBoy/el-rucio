using ElRucio.Shared.Models;
using ElRucio.Shared.Options;

namespace ElRucio.Platform.Tests;

public class InboundChatProcessorTests
{
    [Fact]
    public async Task ProcessAsync_NotAllowlisted_ReturnsFalse()
    {
        var fixture = await InboundTestFixture.CreateAsync(new ElRucioOptions
        {
            DataDir = Path.Combine(Path.GetTempPath(), "elrucio-inbound-tests", Guid.NewGuid().ToString("N")),
            AllowedChatIds = ["telegram:allowed-chat"]
        });

        var sent = new List<string>();

        var handled = await fixture.Processor.ProcessAsync(
            new InboundMessage(
                "telegram:blocked-chat",
                "hello",
                [],
                DateTimeOffset.UtcNow,
                Provider: "telegram",
                ConversationId: "blocked-chat"),
            (_, text, _) =>
            {
                sent.Add(text);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.False(handled);
        Assert.Empty(sent);
    }

    [Fact]
    public async Task ProcessAsync_CommandMessage_SendsCommandOutput()
    {
        var fixture = await InboundTestFixture.CreateAsync();
        var sent = new List<string>();

        var handled = await fixture.Processor.ProcessAsync(
            new InboundMessage(
                "telegram:8039589809",
                "/start",
                [],
                DateTimeOffset.UtcNow,
                Provider: "telegram",
                ConversationId: "8039589809"),
            (_, text, _) =>
            {
                sent.Add(text);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.True(handled);
        Assert.Contains(sent, x => x.Contains("El Rucio connected.", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProcessAsync_PlainPrompt_SendsOrchestratorResponse()
    {
        var fixture = await InboundTestFixture.CreateAsync();
        var sent = new List<string>();

        var handled = await fixture.Processor.ProcessAsync(
            new InboundMessage(
                "telegram:8039589809",
                "tell me a short joke",
                [],
                DateTimeOffset.UtcNow,
                Provider: "telegram",
                ConversationId: "8039589809"),
            (_, text, _) =>
            {
                sent.Add(text);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.True(handled);
        Assert.Contains("agent-response", sent.Single());
    }
}
