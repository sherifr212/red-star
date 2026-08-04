using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RedStar.Base;
using RedStar.Base.Telemetry;

namespace RedStar.Cli.Telemetry;

/// <summary>
/// The only place in RedStar that references the OpenTelemetry SDK/exporter packages -- builds the
/// <see cref="TracerProvider"/>/<see cref="MeterProvider"/>/<see cref="ILoggerFactory"/> that export to an
/// OTLP collector (e.g. the standalone Aspire Dashboard) and assigns the logger factory to
/// <see cref="RedStarTelemetry.LoggerFactory"/> so <c>RedStar.Base</c> code picks it up. No console or file
/// logging provider is ever added -- structured logs go to OTLP only, matching the terminal UI staying
/// exactly as it was.
/// </summary>
internal static class TelemetryBootstrapper
{
    /// <summary>
    /// Configures OTel export per <paramref name="options"/>.<c>Otel</c>, or returns a no-op
    /// <see cref="IDisposable"/> when disabled. Dispose flushes and shuts down every provider -- callers
    /// should wrap the whole run in a <c>using</c> so telemetry is flushed on normal exit, an unhandled
    /// exception, or Ctrl+C cancellation.
    /// </summary>
    public static IDisposable Configure(RedStarOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Otel.Enabled)
        {
            return NullDisposable.Instance;
        }

        var endpoint = new Uri(options.Otel.Endpoint);
        var resourceBuilder = ResourceBuilder.CreateDefault().AddService(RedStarTelemetry.ServiceName);

        var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(resourceBuilder)
            .AddSource(RedStarTelemetry.ServiceName)
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(o => o.Endpoint = endpoint)
            .Build();

        var meterProvider = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(resourceBuilder)
            .AddMeter(RedStarTelemetry.ServiceName)
            .AddMeter("System.Net.Http")
            .AddOtlpExporter(o => o.Endpoint = endpoint)
            .Build();

        var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder.AddOpenTelemetry(o =>
        {
            o.SetResourceBuilder(resourceBuilder);
            o.IncludeFormattedMessage = true;
            o.IncludeScopes = true;
            o.AddOtlpExporter(exporter => exporter.Endpoint = endpoint);
        }));
        RedStarTelemetry.LoggerFactory = loggerFactory;

        return new CompositeDisposable(tracerProvider, meterProvider, loggerFactory);
    }

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();
        public void Dispose()
        {
        }
    }

    private sealed class CompositeDisposable(params IDisposable[] disposables) : IDisposable
    {
        public void Dispose()
        {
            foreach (var disposable in disposables)
            {
                disposable.Dispose();
            }

            RedStarTelemetry.LoggerFactory = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
        }
    }
}
