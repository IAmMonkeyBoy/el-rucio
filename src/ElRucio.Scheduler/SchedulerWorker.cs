using Cronos;
using ElRucio.Memory.Sqlite;
using ElRucio.Shared.Contracts;
using ElRucio.Shared.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ElRucio.Scheduler;

public sealed class SchedulerWorker(
    IScheduledTaskStore taskStore,
    SqliteDbInitializer dbInitializer,
    IAgentRuntime agentRuntime,
    IOutboundMessenger outboundMessenger,
    IOptions<SchedulerOptions> schedulerOptions,
    ILogger<SchedulerWorker> logger) : BackgroundService
{
    private readonly SchedulerOptions _options = schedulerOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await dbInitializer.InitializeAsync(stoppingToken);

        if (!_options.Enabled)
        {
            logger.LogInformation("Scheduler disabled by configuration");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var due = await taskStore.GetDueAsync(now, stoppingToken);
                foreach (var task in due)
                {
                    var response = await agentRuntime.SendPromptAsync(task.ChatId, task.Prompt, stoppingToken);
                    await outboundMessenger.SendTextAsync(task.ChatId, $"[Scheduled:{task.Id}]\n{response}", stoppingToken);

                    if (task.Cron.StartsWith("once:", StringComparison.OrdinalIgnoreCase))
                    {
                        await taskStore.SetEnabledAsync(task.Id, false, stoppingToken);
                        await taskStore.TouchRunAsync(task.Id, now, now, stoppingToken);
                        continue;
                    }

                    var cron = CronExpression.Parse(task.Cron);
                    var next = cron.GetNextOccurrence(now, TimeZoneInfo.Utc) ?? now.AddMinutes(5);
                    await taskStore.TouchRunAsync(task.Id, now, next, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduler loop error");
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _options.PollSeconds)), stoppingToken);
        }
    }
}
