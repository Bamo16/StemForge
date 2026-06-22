using System.Text.Json.Serialization;

namespace StemForge.Core.Separation;

/// <summary>
/// A phase of a running separation, carried on the <see cref="PhaseEvent"/>. The closed set is
/// declared in <c>tools/driver_protocol.json</c> and mirrored here; a contract test fails the build
/// on drift. Deserializing an undeclared phase throws, which the dispatcher treats as a loud
/// contract violation rather than silently defaulting.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DriverPhase>))]
internal enum DriverPhase
{
    [JsonStringEnumMemberName("downloading_model")]
    DownloadingModel,

    [JsonStringEnumMemberName("loading_model")]
    LoadingModel,

    [JsonStringEnumMemberName("separating")]
    Separating,

    [JsonStringEnumMemberName("ensembling")]
    Ensembling,
}

/// <summary>
/// One inbound line from <c>separator_driver.py</c>. Each event is a distinct derived record
/// dispatched by type; the wire discriminator is the <c>event</c> field (always emitted first). The
/// vocabulary is closed and co-versioned with the driver (declared in
/// <c>tools/driver_protocol.json</c>), so an unrecognized discriminator throws on deserialization,
/// which is the intended fail-loud behavior for a contract violation rather than something to
/// tolerate. See ADR 0008.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "event")]
[JsonDerivedType(typeof(ReadyEvent), "ready")]
[JsonDerivedType(typeof(PhaseEvent), "phase")]
[JsonDerivedType(typeof(ProgressEvent), "progress")]
[JsonDerivedType(typeof(LogEvent), "log")]
[JsonDerivedType(typeof(StemWrittenEvent), "stem_written")]
[JsonDerivedType(typeof(JobCompletedEvent), "job_completed")]
[JsonDerivedType(typeof(JobFailedEvent), "job_failed")]
[JsonDerivedType(typeof(JobCancelledEvent), "job_cancelled")]
[JsonDerivedType(typeof(ErrorEvent), "error")]
[JsonDerivedType(typeof(ByeEvent), "bye")]
internal abstract record DriverEvent;

/// <summary>Driver is up and the inference environment is initialized.</summary>
internal sealed record ReadyEvent : DriverEvent
{
    public string? Device { get; init; }
    public string? SeparatorVersion { get; init; }
}

/// <summary>A progress sub-state of the running job. Fields populated depend on the phase.</summary>
internal sealed record PhaseEvent : DriverEvent
{
    public DriverPhase Phase { get; init; }
    public string? Model { get; init; }
    public int? ModelIndex { get; init; }
    public int? ModelCount { get; init; }
    public bool? Cached { get; init; }
    public string? Stem { get; init; }
}

/// <summary>A progress tick (current/total), rendered as a percentage.</summary>
internal sealed record ProgressEvent : DriverEvent
{
    public int? Current { get; init; }
    public int? Total { get; init; }
    public bool? Final { get; init; }
}

/// <summary>A forwarded structured log line from the separator.</summary>
internal sealed record LogEvent : DriverEvent
{
    public string? Level { get; init; }
    public string? Message { get; init; }
}

/// <summary>One output stem was written to disk.</summary>
internal sealed record StemWrittenEvent : DriverEvent
{
    public string? Stem { get; init; }
    public string? Path { get; init; }
}

/// <summary>The job finished successfully.</summary>
internal sealed record JobCompletedEvent : DriverEvent
{
    public List<DriverJobOutput>? Outputs { get; init; }
    public List<DriverJobOutput>? Discarded { get; init; }
    public double? DurationSeconds { get; init; }
}

/// <summary>The job failed with an error (and optional traceback).</summary>
internal sealed record JobFailedEvent : DriverEvent
{
    public string? Error { get; init; }
    public string? Traceback { get; init; }
}

/// <summary>The driver acknowledged a cancel request and stopped the job.</summary>
internal sealed record JobCancelledEvent : DriverEvent;

/// <summary>A protocol-level problem (e.g. an unparseable or unknown command).</summary>
internal sealed record ErrorEvent : DriverEvent
{
    public string? Error { get; init; }
}

/// <summary>The driver is exiting cleanly.</summary>
internal sealed record ByeEvent : DriverEvent;

/// <summary>One stem entry inside a <see cref="JobCompletedEvent"/>'s outputs/discarded arrays.</summary>
internal sealed record DriverJobOutput
{
    public string? Stem { get; init; }
    public string? Path { get; init; }
}

/// <summary>
/// Source-generated serializer context for inbound driver events. Metadata-based source generation
/// (the default for deserialization) is what enables the polymorphic dispatch above. The snake_case
/// naming policy matches the Python side's JSON keys and is co-located with the records it describes.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(DriverEvent))]
internal sealed partial class DriverJsonContext : JsonSerializerContext { }
