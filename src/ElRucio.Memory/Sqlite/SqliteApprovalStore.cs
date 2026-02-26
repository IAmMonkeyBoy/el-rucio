using ElRucio.Shared.Contracts;
using ElRucio.Shared.Models;

namespace ElRucio.Memory.Sqlite;

public sealed class SqliteApprovalStore(SqliteDb db) : IApprovalStore
{
    public async Task QueueAsync(ApprovalRequest request, CancellationToken cancellationToken)
    {
        await using var connection = db.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO approvals(id, chat_id, session_id, kind, payload_json, status, created_utc)
VALUES($id, $chatId, $sessionId, $kind, $payload, $status, $createdUtc)";
        command.Parameters.AddWithValue("$id", request.Id);
        command.Parameters.AddWithValue("$chatId", request.ChatId);
        command.Parameters.AddWithValue("$sessionId", request.SessionId);
        command.Parameters.AddWithValue("$kind", request.Kind);
        command.Parameters.AddWithValue("$payload", request.PayloadJson);
        command.Parameters.AddWithValue("$status", request.Status);
        command.Parameters.AddWithValue("$createdUtc", request.CreatedUtc.UtcDateTime.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ApprovalRequest?> GetPendingAsync(string approvalId, string chatId, CancellationToken cancellationToken)
    {
        await using var connection = db.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT id, chat_id, session_id, kind, payload_json, status, created_utc
FROM approvals
WHERE id = $id AND chat_id = $chatId AND status = 'pending'
LIMIT 1";
        command.Parameters.AddWithValue("$id", approvalId);
        command.Parameters.AddWithValue("$chatId", chatId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ApprovalRequest(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            DateTimeOffset.Parse(reader.GetString(6)));
    }

    public async Task SetStatusAsync(string approvalId, string status, CancellationToken cancellationToken)
    {
        await using var connection = db.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE approvals SET status = $status WHERE id = $id";
        command.Parameters.AddWithValue("$id", approvalId);
        command.Parameters.AddWithValue("$status", status);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
