using System.Collections.Concurrent;
using System.Text;
using ElRucio.Shared.Contracts;
using ElRucio.Shared.Models;
using ElRucio.Shared.Options;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ElRucio.Agent;

public sealed class CopilotAgentRuntime(
    IOptions<CopilotOptions> options,
    ISessionStore sessionStore,
    ILogger<CopilotAgentRuntime> logger) : IAgentRuntime, IAsyncDisposable
{
    private readonly CopilotOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, CopilotSession> _liveSessions = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    private CopilotClient? _client;
    private bool _started;

    public async Task<string> SendPromptAsync(string chatId, string prompt, CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken);
        var session = await GetOrCreateSessionAsync(chatId, cancellationToken);

        var responseBuilder = new StringBuilder();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sub = session.On(ev =>
        {
            if (ev is AssistantMessageEvent assistantEvent && !string.IsNullOrWhiteSpace(assistantEvent.Data.Content))
            {
                responseBuilder.Append(assistantEvent.Data.Content);
            }

            if (ev is SessionIdleEvent)
            {
                done.TrySetResult();
            }

            if (ev is SessionErrorEvent errorEvent)
            {
                done.TrySetException(new InvalidOperationException(errorEvent.Data.Message));
            }
        });

        await session.SendAsync(new MessageOptions { Prompt = prompt });
        await done.Task.WaitAsync(cancellationToken);

        var reply = responseBuilder.ToString();
        if (string.IsNullOrWhiteSpace(reply))
        {
            reply = "No response produced by Copilot runtime.";
        }

        return reply;
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (_started)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_started)
            {
                return;
            }

            var clientOptions = new CopilotClientOptions();
            if (!string.IsNullOrWhiteSpace(_options.CliPath))
            {
                clientOptions.CliPath = _options.CliPath;
            }

            if (!string.IsNullOrWhiteSpace(_options.CliUrl))
            {
                clientOptions.CliUrl = _options.CliUrl;
            }

            _client = new CopilotClient(clientOptions);
            await _client.StartAsync();
            _started = true;
            logger.LogInformation("Copilot SDK client started");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<CopilotSession> GetOrCreateSessionAsync(string chatId, CancellationToken cancellationToken)
    {
        if (_liveSessions.TryGetValue(chatId, out var existing))
        {
            return existing;
        }

        if (_client is null)
        {
            throw new InvalidOperationException("Copilot client is not initialized");
        }

        var binding = await sessionStore.GetAsync(chatId, cancellationToken);
        CopilotSession session;

        if (binding is not null)
        {
            try
            {
                session = await _client.ResumeSessionAsync(binding.SessionId);
                await sessionStore.UpsertAsync(binding with { LastActiveUtc = DateTimeOffset.UtcNow }, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to resume Copilot session {SessionId}; creating a new session", binding.SessionId);
                session = await _client.CreateSessionAsync(new SessionConfig
                {
                    Model = _options.Model,
                    Streaming = true
                });

                var recovered = new SessionBinding(chatId, session.SessionId, binding.CreatedUtc, DateTimeOffset.UtcNow);
                await sessionStore.UpsertAsync(recovered, cancellationToken);
            }
        }
        else
        {
            session = await _client.CreateSessionAsync(new SessionConfig
            {
                Model = _options.Model,
                Streaming = true
            });

            var created = new SessionBinding(chatId, session.SessionId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            await sessionStore.UpsertAsync(created, cancellationToken);
        }

        _liveSessions[chatId] = session;
        return session;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _liveSessions.Values)
        {
            await session.DisposeAsync();
        }

        _liveSessions.Clear();

        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        _gate.Dispose();
    }
}
