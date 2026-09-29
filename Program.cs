using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Configuration;
using Scai.OpcUa.Substitute;

const string AppName = "Scai.OpcUa.Substitute";
const string ConfigSection = "Scai.OpcUa.Substitute";

using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
using var telemetry = new ConsoleTelemetry(loggerFactory);

Console.WriteLine("OPC UA Dynamic Server (Scai.OpcUa.Substitute)");

ApplicationInstance.MessageDlg = new ApplicationMessageDlg();
var app = new ApplicationInstance(telemetry)
{
    ApplicationName = AppName,
    ApplicationType = ApplicationType.Server,
    ConfigSectionName = ConfigSection
};

await app.LoadApplicationConfigurationAsync(false).ConfigureAwait(false);

// Auto-accept for easy development/testing — remove in production.
app.ApplicationConfiguration!.SecurityConfiguration.AutoAcceptUntrustedCertificates = true;

await app.CheckApplicationInstanceCertificatesAsync(false).ConfigureAwait(false);

using var server = new SubstituteServer();
await app.StartAsync(server).ConfigureAwait(false);

Console.WriteLine("Server started. Endpoints:");
foreach (string? ep in server.GetEndpoints().ConvertAll(e => e.EndpointUrl).ToList().Distinct())
{
    Console.WriteLine("  {0}", ep);
}

Console.WriteLine("Press Ctrl-C to stop.");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    await Task.Delay(Timeout.Infinite, cts.Token).ConfigureAwait(false);
}
catch (OperationCanceledException) { }

Console.WriteLine("Stopping...");
await server.StopAsync(default).ConfigureAwait(false);

// Minimal ITelemetryContext implementation wrapping an existing ILoggerFactory.
file sealed class ConsoleTelemetry(ILoggerFactory loggerFactory) : ITelemetryContext, IDisposable
{
    private readonly ActivitySource _activitySource = new("Scai.OpcUa.Substitute", "1.0.0");
    private readonly Meter _meter = new("Scai.OpcUa.Substitute", "1.0.0");

    public ILoggerFactory LoggerFactory { get; } = loggerFactory;
    public ActivitySource ActivitySource => _activitySource;
    public Meter CreateMeter() => _meter;

    public void Dispose()
    {
        _activitySource.Dispose();
        _meter.Dispose();
    }
}

file sealed class ApplicationMessageDlg : IApplicationMessageDlg
{
    public override void Message(string text, bool ask = false) => Console.WriteLine(text);
    public override Task<bool> ShowAsync() => Task.FromResult(true);
}
