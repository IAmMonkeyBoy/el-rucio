using Microsoft.Extensions.DependencyInjection;

namespace ElRucio.Scheduler;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddElRucioScheduler(this IServiceCollection services)
    {
        services.AddHostedService<SchedulerWorker>();
        return services;
    }
}
