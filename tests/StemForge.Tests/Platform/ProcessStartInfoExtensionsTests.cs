using System.Diagnostics;

namespace StemForge.Tests.Platform;

public sealed class ProcessStartInfoExtensionsTests
{
    [Fact]
    public void WithEnvironmentVariable_SetsTheValueAndReturnsTheSameInstance()
    {
        var info = new ProcessStartInfo("tool");

        var result = info.WithEnvironmentVariable("SF_TEST_VAR", "one");

        Assert.Same(info, result);
        Assert.Equal("one", info.Environment["SF_TEST_VAR"]);
    }

    [Fact]
    public void WithEnvironmentVariable_ExistingKey_IsOverwritten()
    {
        var info = new ProcessStartInfo("tool").WithEnvironmentVariable("SF_TEST_VAR", "old");

        info.WithEnvironmentVariable("SF_TEST_VAR", "new");

        Assert.Equal("new", info.Environment["SF_TEST_VAR"]);
    }

    [Fact]
    public void WithEnvironmentVariable_NullValue_RemovesTheKey()
    {
        var info = new ProcessStartInfo("tool").WithEnvironmentVariable("SF_TEST_VAR", "set");

        info.WithEnvironmentVariable("SF_TEST_VAR", null);

        Assert.False(info.Environment.ContainsKey("SF_TEST_VAR"));
    }

    [Fact]
    public void WithEnvironmentVariables_AppliesEveryEntry()
    {
        var info = new ProcessStartInfo("tool").WithEnvironmentVariable("SF_GONE", "x");

        info.WithEnvironmentVariables(("SF_A", "1"), ("SF_B", "2"), ("SF_GONE", null));

        Assert.Equal("1", info.Environment["SF_A"]);
        Assert.Equal("2", info.Environment["SF_B"]);
        Assert.False(info.Environment.ContainsKey("SF_GONE"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void WithEnvironmentVariable_BlankKey_Throws(string key) =>
        Assert.ThrowsAny<ArgumentException>(() =>
            new ProcessStartInfo("tool").WithEnvironmentVariable(key, "v")
        );
}
