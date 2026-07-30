namespace StemForge.Tests.Catalog;

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
    public void ParseModels_ValidJson_ReturnsModels()
    {
        var models = ModelCatalogService.ParseModels(ValidJson);

        Assert.Equal(2, models.Count);
        var kim = models.Single(m => m.Filename == "Kim_Vocal_2.onnx");
        Assert.Equal("MDX", kim.Architecture);
        Assert.Equal("Kim Vocal 2", kim.FriendlyName);
        Assert.Equal(2, kim.Stems.Count);

        var vocalStem = kim.Stems.Single(s => s.Name == "vocals");
        Assert.Equal(10.15, vocalStem.Sdr);
    }

    [Fact]
    public void ParseModels_StemsWithoutScores_SdrIsNull()
    {
        var models = ModelCatalogService.ParseModels(ValidJson);
        var ht = models.Single(m => m.Filename == "htdemucs.yaml");

        Assert.Equal(4, ht.Stems.Count);
        Assert.All(ht.Stems, s => Assert.Null(s.Sdr));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{ invalid }")]
    public void ParseModels_InvalidOrEmpty_ReturnsEmpty(string raw)
    {
        Assert.Empty(ModelCatalogService.ParseModels(raw));
    }

    [Fact]
    public void ParseModels_JsonWithLogPrefix_StillParsed()
    {
        var withPrefix = "INFO:audio_separator:listing models\n" + ValidJson + "\nDone.";
        var models = ModelCatalogService.ParseModels(withPrefix);
        Assert.Equal(2, models.Count);
    }

    [Fact]
    public void ParseModels_DuplicateFilename_DedupedFirstWins()
    {
        // Upstream output can repeat the same filename across entries. The first
        // occurrence should win and the duplicate should be dropped.
        var json = """
            {
              "MDX": {
                "Kim Vocal 2": {
                  "filename": "Kim_Vocal_2.onnx",
                  "stems": ["vocals", "instrumental"]
                },
                "Kim Vocal 2 (duplicate)": {
                  "filename": "Kim_Vocal_2.onnx",
                  "stems": ["vocals"]
                }
              }
            }
            """;

        var models = ModelCatalogService.ParseModels(json);

        Assert.Single(models);
        var only = models.Single(m => m.Filename == "Kim_Vocal_2.onnx");
        Assert.Equal("Kim Vocal 2", only.FriendlyName);
        Assert.Equal(2, only.Stems.Count);
    }

    [Fact]
    public void ParseModels_ScoresMixObjectsAndScalarMetrics_ParsesAndIgnoresScalars()
    {
        // Real upstream data: median_scores can carry a scalar metric (seconds_per_minute_m3)
        // alongside the per-stem score objects. The whole catalog must still parse, the scalar
        // must be ignored, and real stem SDRs must come through. Regression for the empty-catalog
        // failure where one scalar value tripped the deserializer and silently emptied the list.
        var json = """
            {
              "MDXC": {
                "Roformer Model: BS-Roformer-Viperx-1053": {
                  "filename": "model_bs_roformer_ep_937_sdr_10.5309.ckpt",
                  "scores": {
                    "vocals": { "SDR": 10.5309 },
                    "instrumental": { "SDR": 16.4 },
                    "seconds_per_minute_m3": 8.6
                  },
                  "stems": ["vocals", "instrumental"],
                  "target_stem": null
                }
              }
            }
            """;

        var models = ModelCatalogService.ParseModels(json);

        var model = Assert.Single(models);
        Assert.Equal(2, model.Stems.Count);
        Assert.Equal(10.5309, model.Stems.Single(s => s.Name == "vocals").Sdr);
        Assert.Equal(16.4, model.Stems.Single(s => s.Name == "instrumental").Sdr);
        // The scalar metric is not a stem and must not appear among the stems.
        Assert.DoesNotContain(model.Stems, s => s.Name == "seconds_per_minute_m3");
    }

    [Fact]
    public void ParseModels_ModelMissingFilename_Skipped()
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
        Assert.Empty(ModelCatalogService.ParseModels(json));
    }

    [Fact]
    public void ParseModels_ListModelsScriptShape_ParsesFilenameStemsAndScores()
    {
        // Shape emitted by the lightweight tools/list_models.py one-shot: each entry carries
        // filename, stems, a "target_stem" field (ignored by the parser), and scores derived
        // from the bundled median_scores. Guards that the static-data path stays parseable.
        var json = """
            {
              "MDXC": {
                "Roformer Model: BS-Roformer-Viperx-1297": {
                  "filename": "model_bs_roformer_ep_317_sdr_12.9755.ckpt",
                  "scores": {
                    "vocals": { "SDR": 12.9755 },
                    "instrumental": { "SDR": 16.9171 }
                  },
                  "stems": ["vocals", "instrumental"],
                  "target_stem": null
                }
              },
              "VR": {
                "No Score Model": {
                  "filename": "1_HP-UVR.pth",
                  "scores": {},
                  "stems": ["vocals", "instrumental"],
                  "target_stem": null
                }
              }
            }
            """;

        var models = ModelCatalogService.ParseModels(json);

        Assert.Equal(2, models.Count);

        var roformer = models.Single(m =>
            m.Filename == "model_bs_roformer_ep_317_sdr_12.9755.ckpt"
        );
        Assert.Equal("MDXC", roformer.Architecture);
        Assert.Equal(12.9755, roformer.Stems.Single(s => s.Name == "vocals").Sdr);

        // Empty "scores" object leaves every stem SDR null but still lists the stems.
        var noScore = models.Single(m => m.Filename == "1_HP-UVR.pth");
        Assert.Equal(2, noScore.Stems.Count);
        Assert.All(noScore.Stems, s => Assert.Null(s.Sdr));
    }
}
