using System.Reflection;

namespace StemForge.Tests.Services;

/// <summary>
/// Tests for the private termination-message builder and bounded buffer inside
/// SeparatorDriverService. Because these members are private (not internal), they are invoked via
/// reflection. The dispatch path is covered separately in
/// <see cref="SeparatorDriverServiceDispatchTests"/>; preset parsing in
/// <see cref="PresetCatalogServiceTests"/>.
/// </summary>
public sealed class SeparatorDriverServiceTests
{
    private static readonly BindingFlags _privateStatic =
        BindingFlags.NonPublic | BindingFlags.Static;

    private static readonly Type _type = typeof(SeparatorDriverService);

    // ── BuildTerminationMessage ──────────────────────────────────────────────

    [Fact]
    public void BuildTerminationMessage_IncludesExitCodeAndStderrTail()
    {
        var message = InvokeBuildTerminationMessage(
            exitCode: 137,
            stderrTail: ["loading model", "CUDA out of memory", "torch.OutOfMemoryError"]
        );

        Assert.Contains("137", message);
        Assert.Contains("CUDA out of memory", message);
        Assert.Contains("torch.OutOfMemoryError", message);
    }

    [Fact]
    public void BuildTerminationMessage_UnknownExitCode_StillReportsTail()
    {
        var message = InvokeBuildTerminationMessage(exitCode: null, stderrTail: ["fatal error"]);

        Assert.Contains("unknown", message);
        Assert.Contains("fatal error", message);
    }

    [Fact]
    public void BuildTerminationMessage_NoStderr_ReportsCodeWithoutTail()
    {
        var message = InvokeBuildTerminationMessage(exitCode: 1, stderrTail: []);

        Assert.Contains("1", message);
        Assert.Contains("no output", message);
    }

    [Fact]
    public void BuildTerminationMessage_NativeCrashNoStderr_ReportsActivityTail()
    {
        // A native crash (e.g. 0xC0000409) writes nothing to stderr; the structured
        // activity tail is the only record of what the driver was doing when it died.
        var message = InvokeBuildTerminationMessage(
            exitCode: -1073740791,
            stderrTail: [],
            activityTail: ["Starting separation process", "Detected input bit depth: 24-bit"]
        );

        Assert.Contains("-1073740791", message);
        Assert.Contains("Detected input bit depth: 24-bit", message);
        Assert.DoesNotContain("no output", message);
    }

    // ── BoundedLineBuffer ──────────────────────────────────────────────────

    [Fact]
    public void BoundedLineBuffer_KeepsOnlyMostRecentLinesUpToCapacity()
    {
        var buffer = NewBoundedBuffer(capacity: 3);

        for (var i = 1; i <= 100; i++)
            BufferAdd(buffer, $"line {i}");

        var snapshot = BufferSnapshot(buffer);

        Assert.Equal(3, snapshot.Count);
        Assert.Equal(["line 98", "line 99", "line 100"], snapshot);
    }

    [Fact]
    public void BoundedLineBuffer_BelowCapacity_PreservesOrder()
    {
        var buffer = NewBoundedBuffer(capacity: 30);

        BufferAdd(buffer, "first");
        BufferAdd(buffer, "second");

        Assert.Equal(["first", "second"], BufferSnapshot(buffer));
    }

    [Fact]
    public void BoundedLineBuffer_Clear_EmptiesBuffer()
    {
        var buffer = NewBoundedBuffer(capacity: 5);
        BufferAdd(buffer, "stale");

        BufferClear(buffer);

        Assert.Empty(BufferSnapshot(buffer));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string InvokeBuildTerminationMessage(
        int? exitCode,
        IReadOnlyList<string> stderrTail,
        IReadOnlyList<string>? activityTail = null
    )
    {
        var method = _type.GetMethod("BuildTerminationMessage", _privateStatic)!;
        return (string)
            method.Invoke(null, [exitCode, stderrTail, activityTail ?? (IReadOnlyList<string>)[]])!;
    }

    private static readonly Type _bufferType = _type.GetNestedType(
        "BoundedLineBuffer",
        BindingFlags.NonPublic
    )!;

    private static object NewBoundedBuffer(int capacity) =>
        Activator.CreateInstance(_bufferType, [capacity])!;

    private static void BufferAdd(object buffer, string line) =>
        _bufferType.GetMethod("Add")!.Invoke(buffer, [line]);

    private static void BufferClear(object buffer) =>
        _bufferType.GetMethod("Clear")!.Invoke(buffer, []);

    private static IReadOnlyList<string> BufferSnapshot(object buffer) =>
        (IReadOnlyList<string>)_bufferType.GetMethod("Snapshot")!.Invoke(buffer, [])!;
}
