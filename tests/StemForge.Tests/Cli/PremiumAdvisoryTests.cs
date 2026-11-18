using StemForge.Cli;

namespace StemForge.Tests.Cli;

public sealed class PremiumAdvisoryTests
{
    [Theory]
    [InlineData(PremiumStatus.NotSignedIn)]
    [InlineData(PremiumStatus.AccountNotPremium)]
    public void For_OutcomeTheUserCanFixBySigningIn_IsAWarningCarryingTheAdvisory(
        PremiumStatus status
    ) =>
        Assert.Equal(
            $"Warning: {PremiumExpectation.AdvisoryFor(status)}",
            PremiumAdvisory.For(status)
        );

    [Theory]
    [InlineData(PremiumStatus.PremiumNotSelected)]
    [InlineData(PremiumStatus.SourceHasNoPremiumAudio)]
    [InlineData(PremiumStatus.NoPremiumLadder)]
    public void For_OutcomeTheUserCannotActOn_IsANoteCarryingTheAdvisory(PremiumStatus status) =>
        Assert.Equal(
            $"Note: {PremiumExpectation.AdvisoryFor(status)}",
            PremiumAdvisory.For(status)
        );

    [Theory]
    [InlineData(PremiumStatus.NotApplicable)]
    [InlineData(PremiumStatus.Premium)]
    public void For_OutcomeNeedingNoComment_IsNull(PremiumStatus status) =>
        Assert.Null(PremiumAdvisory.For(status));

    [Fact]
    public void For_EveryStatus_IsEitherSilentOrPrefixedWithWarningOrNote()
    {
        foreach (var status in Enum.GetValues<PremiumStatus>())
        {
            var line = PremiumAdvisory.For(status);

            Assert.Equal(PremiumExpectation.AdvisoryFor(status) is null, line is null);
            if (line is not null)
                Assert.Matches("^(Warning|Note): ", line);
        }
    }
}
