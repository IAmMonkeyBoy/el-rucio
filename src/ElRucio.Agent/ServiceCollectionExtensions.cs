using ElRucio.Shared.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace ElRucio.Agent;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddElRucioAgent(this IServiceCollection services)
    {
        services.AddSingleton<IAgentRuntime, CopilotAgentRuntime>();
        return services;
    }
}
