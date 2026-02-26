using ElRucio.Shared.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ElRucio.Host.Workers;

public sealed class ServiceInstallerHostedService(
    IOptions<ElRucioOptions> appOptions,
    IOptions<ServiceInstallOptions> installOptions,
    IHostEnvironment environment,
    ILogger<ServiceInstallerHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!installOptions.Value.Enabled)
        {
            return Task.CompletedTask;
        }

        var outputDir = Path.Combine(appOptions.Value.DataDir, "service");
        Directory.CreateDirectory(outputDir);

        var exePath = Path.Combine(environment.ContentRootPath, "ElRucio.Host");
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(Path.Combine(outputDir, "windows-service.txt"),
                $"sc.exe create ElRucio binPath= \"{exePath}\" start= auto\nsc.exe start ElRucio\n");
        }

        if (OperatingSystem.IsLinux())
        {
            File.WriteAllText(Path.Combine(outputDir, "elrucio.service"),
                "[Unit]\nDescription=El Rucio\n\n[Service]\nExecStart=/usr/bin/dotnet /path/to/ElRucio.Host.dll\nRestart=always\n\n[Install]\nWantedBy=default.target\n");
        }

        if (OperatingSystem.IsMacOS())
        {
            File.WriteAllText(Path.Combine(outputDir, "com.elrucio.agent.plist"),
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n<plist version=\"1.0\"><dict><key>Label</key><string>com.elrucio.agent</string><key>ProgramArguments</key><array><string>/usr/local/share/dotnet/dotnet</string><string>/path/to/ElRucio.Host.dll</string></array><key>RunAtLoad</key><true/></dict></plist>");
        }

        logger.LogInformation("Service install artifacts generated at {Dir}", outputDir);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
