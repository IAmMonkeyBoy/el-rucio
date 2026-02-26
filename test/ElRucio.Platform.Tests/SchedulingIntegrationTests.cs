using ElRucio.Memory.Sqlite;
using ElRucio.Platform.Commanding;
using ElRucio.Platform.Orchestration;
using ElRucio.Platform.Telegram;
using ElRucio.Scheduler;
using ElRucio.Shared.Contracts;
using ElRucio.Shared.Models;
using ElRucio.Shared.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ElRucio.Platform.Tests;

public class SchedulingIntegrationTests
{
    [Fact]
    public async Task NaturalLanguageSchedule_CreatesOneShotTask()
    {
        var fixture = await TestFixture.CreateAsync();

        var handled = await fixture.InvokeNaturalScheduleAsync("8039589809", "schedule a task for 5 minutes from now to tell me a short joke");

        Assert.True(handled);

        var items = await fixture.ScheduledTaskStore.ListAsync("telegram:8039589809", CancellationToken.None);
        var created = Assert.Single(items);
        Assert.StartsWith("once:", created.Cron, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tell me a short joke", created.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.True(created.Enabled);
        Assert.Contains(fixture.TelegramHandler.SentMessageBodies, body => body.Contains("Scheduled one-time task", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OneShotScheduler_DisablesTaskAfterRun()
    {
        var fixture = await TestFixture.CreateAsync();

        var task = new ScheduledTaskItem(
            "oneshot01",
            "8039589809",
            "sess01",
            "once:" + DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"),
            "tell me a short joke",
            true,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            null,
            DateTimeOffset.UtcNow.AddMinutes(-2));

        await fixture.ScheduledTaskStore.InsertAsync(task, CancellationToken.None);
        await fixture.RunSchedulerSingleCycleAsync();

        var after = (await fixture.ScheduledTaskStore.ListAsync("8039589809", CancellationToken.None)).Single(x => x.Id == "oneshot01");
        Assert.False(after.Enabled);
        Assert.NotNull(after.LastRunUtc);
        Assert.Contains("[Scheduled:oneshot01]", fixture.Outbound.Messages.Single());
    }

    [Fact]
    public async Task RecurringScheduler_KeepsTaskEnabled()
    {
        var fixture = await TestFixture.CreateAsync();

        var task = new ScheduledTaskItem(
            "recur001",
            "8039589809",
            "sess01",
            "* * * * *",
            "tell me a short joke",
            true,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            null,
            DateTimeOffset.UtcNow.AddMinutes(-2));

        await fixture.ScheduledTaskStore.InsertAsync(task, CancellationToken.None);
        await fixture.RunSchedulerSingleCycleAsync();

        var after = (await fixture.ScheduledTaskStore.ListAsync("8039589809", CancellationToken.None)).Single(x => x.Id == "recur001");
        Assert.True(after.Enabled);
        Assert.NotNull(after.LastRunUtc);
        Assert.True(after.NextRunUtc > DateTimeOffset.UtcNow.AddSeconds(-5));
    }

    private sealed class TestFixture
    {
        public required SqliteScheduledTaskStore ScheduledTaskStore { get; init; }
        public required CapturingOutbound Outbound { get; init; }
        public required CapturingTelegramHandler TelegramHandler { get; init; }
        public required ChatCommandService CommandService { get; init; }
        public required TelegramPollingService TelegramService { get; init; }
        public required SchedulerWorker SchedulerWorker { get; init; }

        public static async Task<TestFixture> CreateAsync()
        {
            var dataDir = Path.Combine(Path.GetTempPath(), "elrucio-scheduling-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataDir);

            var appOptions = Options.Create(new ElRucioOptions
            {
                DataDir = dataDir,
                AllowedChatIds = ["8039589809"]
            });

            var memoryOptions = Options.Create(new MemoryOptions { Mode = "Full" });
            var schedulerOptions = Options.Create(new SchedulerOptions { Enabled = true, PollSeconds = 60 });
            var voiceOptions = Options.Create(new VoiceOptions { Enabled = false });
            var videoOptions = Options.Create(new VideoOptions { Enabled = false });

            var db = new SqliteDb(appOptions);
            var dbInitializer = new SqliteDbInitializer(db, NullLogger<SqliteDbInitializer>.Instance);
            await dbInitializer.InitializeAsync(CancellationToken.None);

            var sessionStore = new SqliteSessionStore(db);
            var memoryStore = new SqliteMemoryStore(db, memoryOptions);
            var approvalStore = new SqliteApprovalStore(db);
            var scheduledTaskStore = new SqliteScheduledTaskStore(db);

            var outbound = new CapturingOutbound();
            var agent = new FakeAgentRuntime();

            var orchestrator = new ChatOrchestrator(
                agent,
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

            var telegramOptions = Options.Create(new TelegramOptions
            {
                BotToken = "dummy-token"
            });

            var telegramHandler = new CapturingTelegramHandler();
            var telegramClient = new TelegramApiClient(new HttpClient(telegramHandler), telegramOptions, NullLogger<TelegramApiClient>.Instance);

            var telegramService = new TelegramPollingService(
                telegramClient,
                commandService,
                orchestrator,
                db,
                sessionStore,
                scheduledTaskStore,
                new NullVoiceTranscriber(),
                new NullVideoAnalyzer(),
                appOptions,
                voiceOptions,
                videoOptions,
                NullLogger<TelegramPollingService>.Instance);

            var schedulerWorker = new SchedulerWorker(
                scheduledTaskStore,
                dbInitializer,
                agent,
                outbound,
                schedulerOptions,
                NullLogger<SchedulerWorker>.Instance);

            return new TestFixture
            {
                ScheduledTaskStore = scheduledTaskStore,
                Outbound = outbound,
                TelegramHandler = telegramHandler,
                CommandService = commandService,
                TelegramService = telegramService,
                SchedulerWorker = schedulerWorker
            };
        }

        public async Task<bool> InvokeNaturalScheduleAsync(string chatId, string text)
        {
            return await CommandService.TryHandleAsync(
                ConversationRef.Telegram(chatId),
                text,
                TelegramService.SendTextAsync,
                CancellationToken.None);
        }

        public async Task RunSchedulerSingleCycleAsync()
        {
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromMilliseconds(300));

            try
            {
                await SchedulerWorker.StartAsync(cts.Token);
                await Task.Delay(350, CancellationToken.None);
            }
            catch
            {
            }
            finally
            {
                await SchedulerWorker.StopAsync(CancellationToken.None);
            }
        }
    }

    private sealed class FakeAgentRuntime : IAgentRuntime
    {
        public Task<string> SendPromptAsync(string chatId, string prompt, CancellationToken cancellationToken)
            => Task.FromResult("scheduled-response");
    }

    private sealed class CapturingOutbound : IOutboundMessenger
    {
        public List<string> Messages { get; } = [];

        public Task SendTextAsync(string chatId, string text, CancellationToken cancellationToken)
        {
            Messages.Add(text);
            return Task.CompletedTask;
        }
    }

    private sealed class NullVoiceTranscriber : IVoiceTranscriber
    {
        public Task<string?> TranscribeAsync(string filePath, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);
    }

    private sealed class NullVideoAnalyzer : IVideoAnalyzer
    {
        public Task<string> AnalyzeAsync(string filePath, CancellationToken cancellationToken)
            => Task.FromResult("no-video");
    }

    private sealed class CapturingTelegramHandler : HttpMessageHandler
    {
        public List<string> SentMessageBodies { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.Contains("/sendMessage", StringComparison.OrdinalIgnoreCase) == true
                && request.Content is not null)
            {
                var body = request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
                SentMessageBodies.Add(body);
            }

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true,\"result\":[]}")
            });
        }
    }
}
