using ElRucio.Memory.Sqlite;
using ElRucio.Shared.Options;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ElRucio.Memory.Tests;

public class UnitTest1
{
    [Fact]
    public async Task BuildMemoryContextBlock_ReturnsStoredMemories()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), "elrucio-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);

        var app = Options.Create(new ElRucioOptions { DataDir = dataDir });
        var memoryOptions = Options.Create(new MemoryOptions { Mode = "Full", FtsTopK = 3, RecencyCount = 5 });

        var db = new SqliteDb(app);
        var initializer = new SqliteDbInitializer(db, NullLogger<SqliteDbInitializer>.Instance);
        await initializer.InitializeAsync(CancellationToken.None);

        var store = new SqliteMemoryStore(db, memoryOptions);
        await store.SaveTurnAsync("chat-1", "session-1", "user", "my favorite editor is VS Code", CancellationToken.None);
        await store.SaveTurnAsync("chat-1", "session-1", "assistant", "Noted your preference.", CancellationToken.None);

        var context = await store.BuildMemoryContextBlockAsync("chat-1", CancellationToken.None);

        Assert.Contains("Memory context", context);
        Assert.Contains("favorite editor", context, StringComparison.OrdinalIgnoreCase);

        await using var connection = db.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT sector FROM memories WHERE role = 'user' LIMIT 1";
        var sector = (string?)await command.ExecuteScalarAsync();
        Assert.Equal("semantic", sector);
    }
}
