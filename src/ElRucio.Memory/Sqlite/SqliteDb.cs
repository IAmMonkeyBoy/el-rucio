using ElRucio.Shared.Options;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace ElRucio.Memory.Sqlite;

public sealed class SqliteDb(IOptions<ElRucioOptions> appOptions)
{
    private readonly ElRucioOptions _appOptions = appOptions.Value;

    public string DbPath => Path.Combine(_appOptions.DataDir, "elrucio.db");

    public SqliteConnection Open()
    {
        Directory.CreateDirectory(_appOptions.DataDir);
        var connection = new SqliteConnection($"Data Source={DbPath};Cache=Shared");
        connection.Open();
        return connection;
    }
}
