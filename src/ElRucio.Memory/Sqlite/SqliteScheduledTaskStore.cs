using ElRucio.Shared.Contracts;
using ElRucio.Shared.Models;

namespace ElRucio.Memory.Sqlite;

public sealed class SqliteScheduledTaskStore(SqliteDb db) : IScheduledTaskStore
{
    public async Task<List<ScheduledTaskItem>> ListAsync(string chatId, CancellationToken cancellationToken)
    {
        await using var connection = db.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
    SELECT id, chat_id, session_id, cron, prompt, enabled, next_run_utc, last_run_utc, created_utc, provider, conversation_id, thread_id, user_id
FROM scheduled_tasks
WHERE chat_id = $chatId
ORDER BY datetime(created_utc) DESC";
        command.Parameters.AddWithValue("$chatId", chatId);

        return await ReadAllAsync(command, cancellationToken);
    }

    public async Task InsertAsync(ScheduledTaskItem item, CancellationToken cancellationToken)
    {
        await using var connection = db.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
    INSERT INTO scheduled_tasks(id, chat_id, session_id, cron, prompt, enabled, next_run_utc, last_run_utc, created_utc, provider, conversation_id, thread_id, user_id)
    VALUES($id, $chatId, $sessionId, $cron, $prompt, $enabled, $nextRunUtc, $lastRunUtc, $createdUtc, $provider, $conversationId, $threadId, $userId)";
        command.Parameters.AddWithValue("$id", item.Id);
        command.Parameters.AddWithValue("$chatId", item.ChatId);
        command.Parameters.AddWithValue("$sessionId", item.SessionId);
        command.Parameters.AddWithValue("$cron", item.Cron);
        command.Parameters.AddWithValue("$prompt", item.Prompt);
        command.Parameters.AddWithValue("$enabled", item.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$nextRunUtc", item.NextRunUtc.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$lastRunUtc", item.LastRunUtc?.UtcDateTime.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$createdUtc", item.CreatedUtc.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$provider", item.Provider);
        command.Parameters.AddWithValue("$conversationId", item.ConversationId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$threadId", item.ThreadId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$userId", item.UserId ?? (object)DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<List<ScheduledTaskItem>> GetDueAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        await using var connection = db.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
    SELECT id, chat_id, session_id, cron, prompt, enabled, next_run_utc, last_run_utc, created_utc, provider, conversation_id, thread_id, user_id
FROM scheduled_tasks
WHERE enabled = 1 AND datetime(next_run_utc) <= datetime($nowUtc)
ORDER BY datetime(next_run_utc) ASC";
        command.Parameters.AddWithValue("$nowUtc", nowUtc.UtcDateTime.ToString("O"));
        return await ReadAllAsync(command, cancellationToken);
    }

    public async Task SetEnabledAsync(string taskId, bool enabled, CancellationToken cancellationToken)
    {
        await using var connection = db.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE scheduled_tasks SET enabled = $enabled WHERE id = $id";
        command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        command.Parameters.AddWithValue("$id", taskId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(string taskId, CancellationToken cancellationToken)
    {
        await using var connection = db.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM scheduled_tasks WHERE id = $id";
        command.Parameters.AddWithValue("$id", taskId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task TouchRunAsync(string taskId, DateTimeOffset? lastRunUtc, DateTimeOffset nextRunUtc, CancellationToken cancellationToken)
    {
        await using var connection = db.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE scheduled_tasks
SET last_run_utc = $lastRunUtc,
    next_run_utc = $nextRunUtc
WHERE id = $id";
        command.Parameters.AddWithValue("$lastRunUtc", lastRunUtc?.UtcDateTime.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$nextRunUtc", nextRunUtc.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$id", taskId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<List<ScheduledTaskItem>> ReadAllAsync(Microsoft.Data.Sqlite.SqliteCommand command, CancellationToken cancellationToken)
    {
        var list = new List<ScheduledTaskItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ScheduledTaskItem(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5) == 1,
                DateTimeOffset.Parse(reader.GetString(6)),
                reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7)),
                DateTimeOffset.Parse(reader.GetString(8)),
                reader.IsDBNull(9) ? "telegram" : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12)));
        }

        return list;
    }
}
