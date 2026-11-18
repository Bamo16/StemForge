namespace StemForge.Tests;

public sealed class PortableFileNameTests
{
    [Theory]
    [InlineData('<')]
    [InlineData('>')]
    [InlineData(':')]
    [InlineData('"')]
    [InlineData('/')]
    [InlineData('\\')]
    [InlineData('|')]
    [InlineData('?')]
    [InlineData('*')]
    [InlineData('\0')]
    [InlineData('\n')]
    [InlineData('\u001f')]
    public void InvalidChars_ReservedAndControlCharacters_AreMatched(char c) =>
        Assert.True(PortableFileName.InvalidChars.Contains(c));

    [Theory]
    [InlineData(' ')]
    [InlineData('.')]
    [InlineData('-')]
    [InlineData('_')]
    [InlineData('(')]
    [InlineData('a')]
    [InlineData('Z')]
    [InlineData('7')]
    [InlineData('é')]
    [InlineData('ä')]
    public void InvalidChars_OrdinaryCharacters_AreNotMatched(char c) =>
        Assert.False(PortableFileName.InvalidChars.Contains(c));

    [Fact]
    public void InvalidChars_StrippingFromATitle_KeepsOnlyPortableCharacters()
    {
        const string title = "AC/DC: Back In Black?";

        var cleaned = string.Concat(title.Where(c => !PortableFileName.InvalidChars.Contains(c)));

        Assert.Equal("ACDC Back In Black", cleaned);
    }
}
