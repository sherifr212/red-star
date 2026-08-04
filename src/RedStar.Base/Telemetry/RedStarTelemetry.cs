using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RedStar.Base.Telemetry;

/// <summary>
/// Ambient telemetry surface for <c>RedStar.Base</c>: an <see cref="ActivitySource"/>/<see cref="Meter"/>
/// pair library code creates spans/metrics against directly (no OTel SDK package referenced here -- see
/// <c>TelemetryBootstrapper</c> in RedStar.Cli, the only place a <c>TracerProvider</c>/<c>MeterProvider</c>
/// is actually built), plus a mutable <see cref="LoggerFactory"/> the CLI sets once at startup. Defaults to
/// a no-op logger factory and an unlistened <see cref="ActivitySource"/> (whose <c>StartActivity</c> calls
/// return null harmlessly) so library code and tests that never call the bootstrapper work unchanged --
/// same ambient-fallback shape as <see cref="Activity.Current"/> itself.
/// </summary>
public static class RedStarTelemetry
{
    public const string ServiceName = "RedStar";

    public static readonly ActivitySource ActivitySource = new(ServiceName);

    public static readonly Meter Meter = new(ServiceName);

    public static readonly Counter<long> RequestCount =
        Meter.CreateCounter<long>("redstar.requests", description: "Number of requests made to the LLM server.");

    public static readonly Histogram<double> RequestDuration =
        Meter.CreateHistogram<double>("redstar.request.duration", unit: "ms", description: "Duration of requests made to the LLM server.");

    public static ILoggerFactory LoggerFactory { get; set; } = NullLoggerFactory.Instance;

    public static ILogger CreateLogger(string categoryName) => LoggerFactory.CreateLogger(categoryName);
}
