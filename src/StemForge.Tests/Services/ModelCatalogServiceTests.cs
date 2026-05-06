using StemForge.Services;

namespace StemForge.Tests.Services;

public sealed class ModelCatalogServiceTests
{
    private const string ValidJson = """
        {
          "MDX": {
            "Kim Vocal 2": {
              "filename": "Kim_Vocal_2.onnx",
              "stems": ["vocals", "instrumental"],
              "scores": {
                "vocals": { "SDR": 10.15 },
                "instrumental": { "SDR": 9.80 }
              }
            }
          },
          "Demucs": {
            "HTDemucs 4 stems": {
              "filename": "htdemucs.yaml",
              "stems": ["vocals", "drums", "bass", "other"]
            }
          }
        }
        """;

    [Fact]
    public void TryParseJson_ValidJson_ReturnsModels()
    {
        var models = ModelCatalogService.TryParseJson(ValidJson);

        Assert.Equal(2, models.Count);
        var kim = models.Single(m => m.Filename == "Kim_Vocal_2.onnx");
        Assert.Equal("MDX", kim.Architecture);
        Assert.Equal("Kim Vocal 2", kim.FriendlyName);
        Assert.Equal(2, kim.Stems.Count);

        var vocalStem = kim.Stems.Single(s => s.Name == "vocals");
        Assert.Equal(10.15, vocalStem.Sdr);
    }

    [Fact]
    public void TryParseJson_StemsWithoutScores_SdrIsNull()
    {
        var models = ModelCatalogService.TryParseJson(ValidJson);
        var ht = models.Single(m => m.Filename == "htdemucs.yaml");

        Assert.Equal(4, ht.Stems.Count);
        Assert.All(ht.Stems, s => Assert.Null(s.Sdr));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{ invalid }")]
    public void TryParseJson_InvalidOrEmpty_ReturnsEmpty(string raw)
    {
        Assert.Empty(ModelCatalogService.TryParseJson(raw));
    }

    [Fact]
    public void TryParseJson_JsonWithLogPrefix_StillParsed()
    {
        var withPrefix = "INFO:audio_separator:listing models\n" + ValidJson + "\nDone.";
        var models = ModelCatalogService.TryParseJson(withPrefix);
        Assert.Equal(2, models.Count);
    }

    [Fact]
    public void TryParseJson_ModelMissingFilename_Skipped()
    {
        var json = """
            {
              "MDX": {
                "No Filename Model": {
                  "stems": ["vocals"]
                }
              }
            }
            """;
        Assert.Empty(ModelCatalogService.TryParseJson(json));
    }
}
