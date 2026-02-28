using ElRucio.Agent;
using ElRucio.Memory.Sqlite;
using ElRucio.Shared.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ElRucio.Platform.Tests;

public class CopilotRuntimeE2eTests
{
    private const string RuntimeMarker = "ELRUCIO_E2E_RUNTIME_OK";

    [Fact]
    public async Task CopilotRuntime_EndToEnd_ReturnsResponse_WhenOptedIn()
    {
        if (!IsEnabled())
        {
            return;
        }

        await using var fixture = await CopilotE2eFixture.CreateAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var response = await fixture.Runtime.SendPromptAsync(
            "telegram:copilot-e2e",
            $"Reply in one short line and include this exact token: {RuntimeMarker}",
            cts.Token);

        Assert.False(string.IsNullOrWhiteSpace(response));
        Assert.Contains(RuntimeMarker, response, StringComparison.OrdinalIgnoreCase);

        var binding = await fixture.SessionStore.GetAsync("telegram:copilot-e2e", CancellationToken.None);
        Assert.NotNull(binding);
        Assert.False(string.IsNullOrWhiteSpace(binding!.SessionId));
    }

    private static bool IsEnabled()
    {
        var enabled = Environment.GetEnvironmentVariable("ELRUCIO_ENABLE_COPILOT_E2E");
        return string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CopilotE2eFixture : IAsyncDisposable
    {
        public required CopilotAgentRuntime Runtime { get; init; }
        public required SqliteSessionStore SessionStore { get; init; }

        public static async Task<CopilotE2eFixture> CreateAsync()
        {
            var appOptions = Options.Create(new ElRucioOptions
            {
                DataDir = Path.Combine(Path.GetTempPath(), "elrucio-copilot-e2e", Guid.NewGuid().ToString("N"))
            });

            Directory.CreateDirectory(appOptions.Value.DataDir);

            var db = new SqliteDb(appOptions);
            var dbInitializer = new SqliteDbInitializer(db, NullLogger<SqliteDbInitializer>.Instance);
            await dbInitializer.InitializeAsync(CancellationToken.None);

            var sessionStore = new SqliteSessionStore(db);
            var copilotOptions = Options.Create(new CopilotOptions
            {
                Model = Environment.GetEnvironmentVariable("ELRUCIO_COPILOT_MODEL") ?? "gpt-5",
                CliPath = Environment.GetEnvironmentVariable("ELRUCIO_COPILOT_CLI_PATH"),
                CliUrl = Environment.GetEnvironmentVariable("ELRUCIO_COPILOT_CLI_URL")
            });

            var runtime = new CopilotAgentRuntime(copilotOptions, sessionStore, NullLogger<CopilotAgentRuntime>.Instance);

            return new CopilotE2eFixture
            {
                Runtime = runtime,
                SessionStore = sessionStore
            };
        }

        public async ValueTask DisposeAsync()
        {
            await Runtime.DisposeAsync();
        }
    }
}
