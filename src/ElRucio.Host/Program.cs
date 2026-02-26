using ElRucio.Agent;
using ElRucio.Host.Workers;
using ElRucio.Memory;
using ElRucio.Platform;
using ElRucio.Scheduler;
using ElRucio.Shared.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
	.AddOptions<ElRucioOptions>()
	.Bind(builder.Configuration.GetSection("ElRucio"))
	.ValidateDataAnnotations()
	.ValidateOnStart();

builder.Services
	.AddOptions<TelegramOptions>()
	.Bind(builder.Configuration.GetSection("Telegram"))
	.ValidateDataAnnotations()
	.ValidateOnStart();

builder.Services
	.AddOptions<CopilotOptions>()
	.Bind(builder.Configuration.GetSection("Copilot"));

builder.Services
	.AddOptions<MemoryOptions>()
	.Bind(builder.Configuration.GetSection("Memory"));

builder.Services
	.AddOptions<SchedulerOptions>()
	.Bind(builder.Configuration.GetSection("Scheduler"));

builder.Services
	.AddOptions<VoiceOptions>()
	.Bind(builder.Configuration.GetSection("Voice"));

builder.Services
	.AddOptions<VideoOptions>()
	.Bind(builder.Configuration.GetSection("Video"));

builder.Services
	.AddOptions<ServiceInstallOptions>()
	.Bind(builder.Configuration.GetSection("ServiceInstall"));

builder.Services.AddElRucioMemory();
builder.Services.AddElRucioAgent();
builder.Services.AddElRucioPlatform();
builder.Services.AddElRucioScheduler();

builder.Services.AddHostedService<DbInitializationHostedService>();
builder.Services.AddHostedService<SalienceDecayHostedService>();
builder.Services.AddHostedService<ServiceInstallerHostedService>();

if (OperatingSystem.IsWindows())
{
	builder.Services.AddWindowsService();
}
else if (OperatingSystem.IsLinux())
{
	builder.Services.AddSystemd();
}

await builder.Build().RunAsync();
