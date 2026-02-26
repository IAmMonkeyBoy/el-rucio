using System.Text;
using ElRucio.Shared.Contracts;
using ElRucio.Shared.Options;
using Microsoft.Extensions.Options;

namespace ElRucio.Memory.Sqlite;

public sealed class SqliteMemoryStore(SqliteDb db, IOptions<MemoryOptions> options) : IMemoryStore
{
    private readonly MemoryOptions _options = options.Value;

    public Task InitializeAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public async Task<string> BuildMemoryContextBlockAsync(string chatId, CancellationToken cancellationToken)
    {
        if (!string.Equals(_options.Mode, "Full", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        await using var connection = db.Open();

        var ftsIds = new List<long>();
        await using (var ftsCommand = connection.CreateCommand())
        {
            ftsCommand.CommandText = @"
SELECT m.id
FROM memories_fts f
JOIN memories m ON m.id = f.rowid
WHERE m.chat_id = $chatId
ORDER BY rank
LIMIT $topK";
            ftsCommand.Parameters.AddWithValue("$chatId", chatId);
            ftsCommand.Parameters.AddWithValue("$topK", _options.FtsTopK);

            await using var reader = await ftsCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                ftsIds.Add(reader.GetInt64(0));
            }
        }

        var selected = new Dictionary<long, string>();
        if (ftsIds.Count > 0)
        {
            var idList = string.Join(",", ftsIds);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT id, role, content FROM memories WHERE id IN ({idList})";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                selected[reader.GetInt64(0)] = $"[{reader.GetString(1)}] {reader.GetString(2)}";
            }
        }

        await using (var recentCommand = connection.CreateCommand())
        {
            recentCommand.CommandText = @"
SELECT id, role, content
FROM memories
WHERE chat_id = $chatId
ORDER BY datetime(created_utc) DESC
LIMIT $recent";
            recentCommand.Parameters.AddWithValue("$chatId", chatId);
            recentCommand.Parameters.AddWithValue("$recent", _options.RecencyCount);

            await using var reader = await recentCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetInt64(0);
                if (!selected.ContainsKey(id))
                {
                    selected[id] = $"[{reader.GetString(1)}] {reader.GetString(2)}";
                }
            }
        }

        if (selected.Count == 0)
        {
            return string.Empty;
        }

        foreach (var id in selected.Keys)
        {
            await using var update = connection.CreateCommand();
            update.CommandText = @"
UPDATE memories
SET salience = MIN($maxSalience, salience + $inc),
    last_access_utc = $nowUtc
WHERE id = $id";
            update.Parameters.AddWithValue("$maxSalience", _options.SalienceMax);
            update.Parameters.AddWithValue("$inc", _options.SalienceAccessIncrement);
            update.Parameters.AddWithValue("$nowUtc", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));
            update.Parameters.AddWithValue("$id", id);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        var sb = new StringBuilder();
        sb.AppendLine("[Memory context]");
        foreach (var line in selected.Values.Take(_options.FtsTopK + _options.RecencyCount))
        {
            sb.AppendLine($"- {line}");
        }

        return sb.ToString();
    }

    public async Task SaveTurnAsync(string chatId, string sessionId, string role, string content, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        await using var connection = db.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO memories(chat_id, session_id, sector, role, content, created_utc, last_access_utc, salience, meta_json)
VALUES($chatId, $sessionId, $sector, $role, $content, $createdUtc, $lastAccessUtc, $salience, NULL)";

        command.Parameters.AddWithValue("$chatId", chatId);
        command.Parameters.AddWithValue("$sessionId", sessionId);
        command.Parameters.AddWithValue("$sector", ClassifySector(content, role));
        command.Parameters.AddWithValue("$role", role);
        command.Parameters.AddWithValue("$content", content);
        command.Parameters.AddWithValue("$createdUtc", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$lastAccessUtc", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$salience", _options.SalienceStart);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ApplyDailyDecayAsync(CancellationToken cancellationToken)
    {
        await using var connection = db.Open();
        await using (var decay = connection.CreateCommand())
        {
            decay.CommandText = @"
UPDATE memories
SET salience = salience * $decay
WHERE julianday('now') - julianday(last_access_utc) >= 1";
            decay.Parameters.AddWithValue("$decay", _options.SalienceDailyDecayRate);
            await decay.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM memories WHERE salience < $threshold";
        delete.Parameters.AddWithValue("$threshold", _options.SalienceDeleteThreshold);
        await delete.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string ClassifySector(string content, string role)
    {
        if (!string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
        {
            return "episodic";
        }

        var lower = content.ToLowerInvariant();
        var semanticSignals = new[] { "my ", "i am", "i prefer", "remember", "always", "never" };
        return semanticSignals.Any(lower.Contains) ? "semantic" : "episodic";
    }
}
