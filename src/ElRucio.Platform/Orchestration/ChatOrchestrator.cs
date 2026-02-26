using System.Text.Json;
using ElRucio.Shared.Contracts;
using ElRucio.Shared.Models;
using ElRucio.Shared.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ElRucio.Platform.Orchestration;

public sealed class ChatOrchestrator(
    IAgentRuntime agentRuntime,
    ISessionStore sessionStore,
    IMemoryStore memoryStore,
    IApprovalStore approvalStore,
    IOptions<ElRucioOptions> appOptions,
    ILogger<ChatOrchestrator> logger)
{
    private static readonly string[] RiskyPatterns = ["rm -rf", "drop table", "delete all", "shutdown", "format disk", "send money"];
    private readonly ElRucioOptions _appOptions = appOptions.Value;

    public async Task<string> HandleUserPromptAsync(string chatId, string prompt, CancellationToken cancellationToken)
    {
        var binding = await sessionStore.GetAsync(chatId, cancellationToken)
                      ?? new SessionBinding(chatId, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        if (IsRisky(prompt))
        {
            var approvalId = Guid.NewGuid().ToString("N")[..8];
            var payload = JsonSerializer.Serialize(new Dictionary<string, string> { ["prompt"] = prompt });
            await approvalStore.QueueAsync(new ApprovalRequest(
                approvalId,
                chatId,
                binding.SessionId,
                "risky_prompt",
                payload,
                "pending",
                DateTimeOffset.UtcNow), cancellationToken);

            return $"Risk gate triggered. Reply `/approve {approvalId}` to run it, or `/cancel {approvalId}` to discard.";
        }

        await sessionStore.UpsertAsync(binding with { LastActiveUtc = DateTimeOffset.UtcNow }, cancellationToken);

        var memoryBlock = await memoryStore.BuildMemoryContextBlockAsync(chatId, cancellationToken);
        var finalPrompt = string.IsNullOrWhiteSpace(memoryBlock)
            ? prompt
            : $"{memoryBlock}\n\n[User message]\n{prompt}";

        var response = await agentRuntime.SendPromptAsync(chatId, finalPrompt, cancellationToken);
        await memoryStore.SaveTurnAsync(chatId, binding.SessionId, "user", prompt, cancellationToken);
        await memoryStore.SaveTurnAsync(chatId, binding.SessionId, "assistant", response, cancellationToken);

        return response;
    }

    public async Task<string> ApproveAsync(string chatId, string approvalId, CancellationToken cancellationToken)
    {
        var pending = await approvalStore.GetPendingAsync(approvalId, chatId, cancellationToken);
        if (pending is null)
        {
            return "No pending approval found with that id.";
        }

        var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(pending.PayloadJson);
        if (payload is null || !payload.TryGetValue("prompt", out var prompt) || string.IsNullOrWhiteSpace(prompt))
        {
            await approvalStore.SetStatusAsync(approvalId, "failed", cancellationToken);
            return "Approval payload invalid; request marked as failed.";
        }

        await approvalStore.SetStatusAsync(approvalId, "approved", cancellationToken);
        logger.LogInformation("Running approved prompt {ApprovalId}", approvalId);
        return await HandleUserPromptAsync(chatId, prompt, cancellationToken);
    }

    public async Task<string> CancelAsync(string chatId, string approvalId, CancellationToken cancellationToken)
    {
        var pending = await approvalStore.GetPendingAsync(approvalId, chatId, cancellationToken);
        if (pending is null)
        {
            return "No pending approval found with that id.";
        }

        await approvalStore.SetStatusAsync(approvalId, "cancelled", cancellationToken);
        return "Approval request cancelled.";
    }

    public string BuildStatusMessage() =>
        $"El Rucio online. RiskLevel={_appOptions.RiskLevel}; MaxResponseChars={_appOptions.MaxResponseChars}.";

    private static bool IsRisky(string prompt)
    {
        var lower = prompt.ToLowerInvariant();
        return RiskyPatterns.Any(lower.Contains);
    }
}
