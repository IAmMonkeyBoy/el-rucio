using ElRucio.Memory.Sqlite;
using ElRucio.Shared.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace ElRucio.Memory;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddElRucioMemory(this IServiceCollection services)
    {
        services.AddSingleton<SqliteDb>();
        services.AddSingleton<SqliteDbInitializer>();
        services.AddSingleton<ISessionStore, SqliteSessionStore>();
        services.AddSingleton<IMemoryStore, SqliteMemoryStore>();
        services.AddSingleton<IApprovalStore, SqliteApprovalStore>();
        services.AddSingleton<IScheduledTaskStore, SqliteScheduledTaskStore>();
        return services;
    }
}
