using ElRucio.Shared.Contracts;
using ElRucio.Shared.Models;

namespace ElRucio.Memory.Sqlite;

public sealed class SqliteSessionStore(SqliteDb db) : ISessionStore
{
    public async Task<SessionBinding?> GetAsync(string chatId, CancellationToken cancellationToken)
    {
        await using var connection = db.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT chat_id, session_id, created_utc, last_active_utc FROM sessions WHERE chat_id = $chatId LIMIT 1";
        command.Parameters.AddWithValue("$chatId", chatId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new SessionBinding(
            reader.GetString(0),
            reader.GetString(1),
            DateTimeOffset.Parse(reader.GetString(2)),
            DateTimeOffset.Parse(reader.GetString(3)));
    }

    public async Task UpsertAsync(SessionBinding binding, CancellationToken cancellationToken)
    {
        await using var connection = db.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO sessions(chat_id, session_id, created_utc, last_active_utc)
VALUES ($chatId, $sessionId, $createdUtc, $lastActiveUtc)
ON CONFLICT(chat_id)
DO UPDATE SET session_id = excluded.session_id, last_active_utc = excluded.last_active_utc";
        command.Parameters.AddWithValue("$chatId", binding.ChatId);
        command.Parameters.AddWithValue("$sessionId", binding.SessionId);
        command.Parameters.AddWithValue("$createdUtc", binding.CreatedUtc.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$lastActiveUtc", binding.LastActiveUtc.UtcDateTime.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(string chatId, CancellationToken cancellationToken)
    {
        await using var connection = db.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM sessions WHERE chat_id = $chatId";
        command.Parameters.AddWithValue("$chatId", chatId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
