using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace CommBank.Observability;

/// <summary>
/// Central, process-wide telemetry primitives. The <see cref="ActivitySource"/> emits custom spans and
/// the <see cref="Meter"/> emits custom business metrics; both names are registered with OpenTelemetry so
/// anything recorded here is exported to traces / Prometheus automatically.
/// </summary>
public static class AppDiagnostics
{
    public const string ServiceName = "CommBank.Api";
    public const string ServiceVersion = "1.0.0";

    public const string ActivitySourceName = ServiceName;
    public const string MeterName = ServiceName;

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, ServiceVersion);

    public static readonly Meter Meter = new(MeterName, ServiceVersion);

    /// <summary>Count of successfully completed fund transfers.</summary>
    public static readonly Counter<long> TransfersCompleted =
        Meter.CreateCounter<long>("commbank.transfers.completed", unit: "{transfer}", description: "Completed fund transfers.");

    /// <summary>Count of transfers rejected by the fraud/AML risk policy.</summary>
    public static readonly Counter<long> TransfersBlocked =
        Meter.CreateCounter<long>("commbank.transfers.blocked", unit: "{transfer}", description: "Transfers blocked by risk policy.");

    /// <summary>Distribution of transfer amounts (for percentile/latency-style dashboards).</summary>
    public static readonly Histogram<double> TransferAmount =
        Meter.CreateHistogram<double>("commbank.transfers.amount", unit: "AUD", description: "Distribution of transfer amounts.");
}
