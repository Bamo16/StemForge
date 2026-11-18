namespace StemForge.Tests.Catalog;

public sealed class PresetStepTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void IsEnsemble_DependsOnlyOnTheModelCount(int modelCount, bool expected)
    {
        var step = new PresetStep(
            StepInput.Source,
            [.. Enumerable.Range(0, modelCount).Select(i => $"model{i}.ckpt")]
        );

        Assert.Equal(expected, step.IsEnsemble);
    }

    [Fact]
    public void IsEnsemble_SingleModelWithAnAlgorithmSet_IsStillNotAnEnsemble()
    {
        var step = new PresetStep(StepInput.Source, ["a.ckpt"], Algorithm: "avg_wave");

        Assert.False(step.IsEnsemble);
    }
}
