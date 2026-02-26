using ElRucio.Memory.Sqlite;
using Microsoft.Extensions.Hosting;

namespace ElRucio.Host.Workers;

public sealed class DbInitializationHostedService(SqliteDbInitializer initializer) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await initializer.InitializeAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
