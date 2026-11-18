using StemForge.Services;
using StemForge.ViewModels;

namespace StemForge.Tests.ViewModels;

public sealed class LogsViewModelTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 17, 9, 30, 15, 250, TimeSpan.Zero);

    private static LogEntry Entry(LogLevel level, string message, string source = "app") =>
        new(Start, level, source, message);

    private static (AppLoggerSink Sink, LogsViewModel Vm) Build(params LogEntry[] entries)
    {
        var sink = new AppLoggerSink();
        foreach (var entry in entries)
            sink.Entries.Add(entry);
        return (sink, new LogsViewModel(sink));
    }

    private static string[] Messages(LogsViewModel vm) =>
        [.. vm.Displayed.Select(entry => entry.Message)];

    [Fact]
    public void Displayed_ByDefault_HidesDebugButShowsTheRest()
    {
        var (_, vm) = Build(
            Entry(LogLevel.Debug, "d"),
            Entry(LogLevel.Info, "i"),
            Entry(LogLevel.Warning, "w"),
            Entry(LogLevel.Error, "e")
        );

        Assert.Equal(["i", "w", "e"], Messages(vm));
    }

    [Fact]
    public void ToggleDebugCommand_RevealsDebugEntriesInTheirOriginalPlace()
    {
        var (_, vm) = Build(Entry(LogLevel.Info, "i"), Entry(LogLevel.Debug, "d"));

        vm.ToggleDebugCommand.Execute(null);

        Assert.Equal(["i", "d"], Messages(vm));
    }

    [Fact]
    public void ToggleInfoCommand_HidesInfoEntries()
    {
        var (_, vm) = Build(Entry(LogLevel.Info, "i"), Entry(LogLevel.Error, "e"));

        vm.ToggleInfoCommand.Execute(null);

        Assert.Equal(["e"], Messages(vm));
    }

    [Fact]
    public void ToggleWarningCommand_HidesWarnings()
    {
        var (_, vm) = Build(Entry(LogLevel.Warning, "w"), Entry(LogLevel.Info, "i"));

        vm.ToggleWarningCommand.Execute(null);

        Assert.Equal(["i"], Messages(vm));
    }

    [Fact]
    public void ToggleErrorCommand_HidesErrors_AndTogglingAgainBringsThemBack()
    {
        var (_, vm) = Build(Entry(LogLevel.Error, "e"), Entry(LogLevel.Info, "i"));

        vm.ToggleErrorCommand.Execute(null);
        Assert.Equal(["i"], Messages(vm));

        vm.ToggleErrorCommand.Execute(null);
        Assert.Equal(["e", "i"], Messages(vm));
    }

    [Fact]
    public void NewEntry_PassingTheFilter_IsAppended()
    {
        var (sink, vm) = Build(Entry(LogLevel.Info, "first"));

        sink.Entries.Add(Entry(LogLevel.Warning, "second"));

        Assert.Equal(["first", "second"], Messages(vm));
    }

    [Fact]
    public void NewEntry_FilteredOut_DoesNotAppear()
    {
        var (sink, vm) = Build(Entry(LogLevel.Info, "first"));

        sink.Entries.Add(Entry(LogLevel.Debug, "noise"));

        Assert.Equal(["first"], Messages(vm));
    }

    [Fact]
    public void NewEntry_HiddenAtTheTime_ShowsUpWhenItsLevelIsTurnedOn()
    {
        var (sink, vm) = Build();
        sink.Entries.Add(Entry(LogLevel.Debug, "noise"));

        vm.ToggleDebugCommand.Execute(null);

        Assert.Equal(["noise"], Messages(vm));
    }

    [Fact]
    public void ClearCommand_EmptiesTheSinkAndTheDisplay()
    {
        var (sink, vm) = Build(Entry(LogLevel.Info, "i"), Entry(LogLevel.Debug, "d"));

        vm.ClearCommand.Execute(null);

        Assert.Empty(sink.Entries);
        Assert.Empty(vm.Displayed);
        Assert.Equal(string.Empty, vm.DisplayedText);
    }

    [Fact]
    public void RemovingASinkEntry_RebuildsTheDisplay()
    {
        var (sink, vm) = Build(Entry(LogLevel.Info, "old"), Entry(LogLevel.Info, "new"));

        sink.Entries.RemoveAt(0);

        Assert.Equal(["new"], Messages(vm));
    }

    [Fact]
    public void DisplayedText_LaysEachEntryOutInFixedWidthColumns()
    {
        var (_, vm) = Build(Entry(LogLevel.Warning, "disk low", source: "setup"));

        var expected = $"{Start.ToString("HH:mm:ss.fff"), -12}  WRN  {"setup", -24}  disk low";
        Assert.Equal(expected, vm.DisplayedText);
    }

    [Fact]
    public void DisplayedText_LongSourceIsCutTo24Characters()
    {
        var source = new string('s', 40);
        var (_, vm) = Build(Entry(LogLevel.Info, "msg", source));

        Assert.Contains($"  {new string('s', 24)}  msg", vm.DisplayedText);
        Assert.DoesNotContain(new string('s', 25), vm.DisplayedText);
    }

    [Fact]
    public void DisplayedText_JoinsEntriesWithNewlines()
    {
        var (_, vm) = Build(Entry(LogLevel.Info, "one"), Entry(LogLevel.Info, "two"));

        var lines = vm.DisplayedText.Split('\n');

        Assert.Equal(2, lines.Length);
        Assert.EndsWith("one", lines[0]);
        Assert.EndsWith("two", lines[1]);
    }

    [Fact]
    public void DisplayedText_ChangeIsRaisedWhenAnEntryIsAdded()
    {
        var (sink, vm) = Build();
        var raised = false;
        vm.PropertyChanged += (_, e) => raised |= e.PropertyName == nameof(vm.DisplayedText);

        sink.Entries.Add(Entry(LogLevel.Info, "x"));

        Assert.True(raised);
    }
}
