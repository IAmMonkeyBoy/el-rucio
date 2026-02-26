using Microsoft.Extensions.Logging;

namespace ElRucio.Memory.Sqlite;

public sealed class SqliteDbInitializer(SqliteDb db, ILogger<SqliteDbInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = db.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE IF NOT EXISTS sessions (
  chat_id TEXT NOT NULL PRIMARY KEY,
  session_id TEXT NOT NULL,
  created_utc TEXT NOT NULL,
  last_active_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS memories (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  chat_id TEXT NOT NULL,
  session_id TEXT NOT NULL,
  sector TEXT NOT NULL,
  role TEXT NOT NULL,
  content TEXT NOT NULL,
  created_utc TEXT NOT NULL,
  last_access_utc TEXT NOT NULL,
  salience REAL NOT NULL,
  meta_json TEXT NULL
);

CREATE INDEX IF NOT EXISTS idx_memories_chat ON memories(chat_id);
CREATE INDEX IF NOT EXISTS idx_memories_sector ON memories(chat_id, sector);
CREATE INDEX IF NOT EXISTS idx_memories_session ON memories(chat_id, session_id, created_utc);

CREATE VIRTUAL TABLE IF NOT EXISTS memories_fts USING fts5(content, content='', tokenize='porter');

CREATE TRIGGER IF NOT EXISTS memories_fts_insert AFTER INSERT ON memories BEGIN
  INSERT INTO memories_fts(rowid, content) VALUES (new.id, new.content);
END;

CREATE TRIGGER IF NOT EXISTS memories_fts_delete AFTER DELETE ON memories BEGIN
  INSERT INTO memories_fts(memories_fts, rowid, content) VALUES ('delete', old.id, old.content);
END;

CREATE TRIGGER IF NOT EXISTS memories_fts_update AFTER UPDATE OF content ON memories BEGIN
  INSERT INTO memories_fts(memories_fts, rowid, content) VALUES ('delete', old.id, old.content);
  INSERT INTO memories_fts(rowid, content) VALUES (new.id, new.content);
END;

CREATE TABLE IF NOT EXISTS approvals (
  id TEXT PRIMARY KEY,
  chat_id TEXT NOT NULL,
  session_id TEXT NOT NULL,
  kind TEXT NOT NULL,
  payload_json TEXT NOT NULL,
  status TEXT NOT NULL,
  created_utc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_approvals_chat ON approvals(chat_id, status);

CREATE TABLE IF NOT EXISTS scheduled_tasks (
  id TEXT PRIMARY KEY,
  chat_id TEXT NOT NULL,
  session_id TEXT NOT NULL,
  cron TEXT NOT NULL,
  prompt TEXT NOT NULL,
  enabled INTEGER NOT NULL,
  next_run_utc TEXT NOT NULL,
  last_run_utc TEXT NULL,
  created_utc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_tasks_next_run ON scheduled_tasks(enabled, next_run_utc);
";

        await command.ExecuteNonQueryAsync(cancellationToken);
        logger.LogInformation("SQLite schema ensured at {Path}", db.DbPath);
    }

}
