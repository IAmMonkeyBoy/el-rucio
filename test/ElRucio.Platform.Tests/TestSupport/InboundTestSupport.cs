using ElRucio.Memory.Sqlite;
using ElRucio.Platform.Commanding;
using ElRucio.Platform.Orchestration;
using ElRucio.Shared.Contracts;
using ElRucio.Shared.Models;
using ElRucio.Shared.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ElRucio.Platform.Tests;

public sealed class InboundTestFixture
{
    public required InboundChatProcessor Processor { get; init; }
    public required SqliteScheduledTaskStore ScheduledTaskStore { get; init; }
    public required ConversationRef DefaultConversation { get; init; }

    public static async Task<InboundTestFixture> CreateAsync(ElRucioOptions? options = null)
    {
        var appOptions = Options.Create(options ?? new ElRucioOptions
        {
            DataDir = Path.Combine(Path.GetTempPath(), "elrucio-inbound-tests", Guid.NewGuid().ToString("N")),
            AllowedChatIds = ["telegram:8039589809"]
        });

        Directory.CreateDirectory(appOptions.Value.DataDir);

        var memoryOptions = Options.Create(new MemoryOptions { Mode = "Full" });
        var voiceOptions = Options.Create(new VoiceOptions { Enabled = false });
        var videoOptions = Options.Create(new VideoOptions { Enabled = false });

        var db = new SqliteDb(appOptions);
        var dbInitializer = new SqliteDbInitializer(db, NullLogger<SqliteDbInitializer>.Instance);
        await dbInitializer.InitializeAsync(CancellationToken.None);

        var sessionStore = new SqliteSessionStore(db);
        var memoryStore = new SqliteMemoryStore(db, memoryOptions);
        var approvalStore = new SqliteApprovalStore(db);
        var scheduledTaskStore = new SqliteScheduledTaskStore(db);

        var orchestrator = new ChatOrchestrator(
            new TestAgentRuntime(),
            sessionStore,
            memoryStore,
            approvalStore,
            appOptions,
            NullLogger<ChatOrchestrator>.Instance);

        var commandService = new ChatCommandService(
            orchestrator,
            db,
            sessionStore,
            scheduledTaskStore,
            appOptions,
            voiceOptions,
            videoOptions);

        var processor = new InboundChatProcessor(
            commandService,
            orchestrator,
            appOptions,
            NullLogger<InboundChatProcessor>.Instance);

        return new InboundTestFixture
        {
            Processor = processor,
            ScheduledTaskStore = scheduledTaskStore,
            DefaultConversation = ConversationRef.Telegram("8039589809")
        };
    }
}

public sealed class InMemoryTransportHarness(IInboundChatProcessor processor, ConversationRef conversation)
{
    public ConversationRef Conversation { get; } = conversation;
    private readonly List<string> _outbound = [];

    public async Task<string> SubmitAsync(string text, CancellationToken cancellationToken = default)
    {
        var before = _outbound.Count;
        await processor.ProcessAsync(
            new InboundMessage(
                Conversation.ChatKey,
                text,
                [],
                DateTimeOffset.UtcNow,
                Provider: Conversation.Provider,
                ConversationId: Conversation.ConversationId,
                ThreadId: Conversation.ThreadId,
                UserId: Conversation.UserId),
            SendAsync,
            cancellationToken);

        if (_outbound.Count == before)
        {
            return string.Empty;
        }

        return _outbound[^1];
    }

    private Task SendAsync(ConversationRef _, string text, CancellationToken __)
    {
        _outbound.Add(text);
        return Task.CompletedTask;
    }
}

public sealed class TestAgentRuntime : IAgentRuntime
{
    public Task<string> SendPromptAsync(string chatId, string prompt, CancellationToken cancellationToken)
        => Task.FromResult("agent-response");
}
