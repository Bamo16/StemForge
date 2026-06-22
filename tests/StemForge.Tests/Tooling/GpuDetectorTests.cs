namespace StemForge.Tests.Tooling;

public sealed class GpuDetectorTests
{
    [Fact]
    public void SuggestVariant_NvidiaGpu_ReturnsCuda()
    {
        var gpus = new[] { new DetectedGpu("NVIDIA GeForce RTX 4090") };
        Assert.Equal(GpuVariant.Cuda, GpuDetector.SuggestVariant(gpus));
    }

    [Fact]
    public void SuggestVariant_AmdGpu_ReturnsDirectML()
    {
        var gpus = new[] { new DetectedGpu("AMD Radeon RX 7900 XT") };
        Assert.Equal(GpuVariant.DirectML, GpuDetector.SuggestVariant(gpus));
    }

    [Fact]
    public void SuggestVariant_IntelGpu_ReturnsDirectML()
    {
        var gpus = new[] { new DetectedGpu("Intel Arc A770") };
        Assert.Equal(GpuVariant.DirectML, GpuDetector.SuggestVariant(gpus));
    }

    [Fact]
    public void SuggestVariant_NoGpus_ReturnsCpu()
    {
        Assert.Equal(GpuVariant.Cpu, GpuDetector.SuggestVariant([]));
    }

    [Fact]
    public void SuggestVariant_UnknownVendor_ReturnsCpu()
    {
        var gpus = new[] { new DetectedGpu("Some Random Display Adapter") };
        Assert.Equal(GpuVariant.Cpu, GpuDetector.SuggestVariant(gpus));
    }

    [Fact]
    public void SuggestVariant_NvidiaPlusAmd_ReturnsCuda()
    {
        var gpus = new[]
        {
            new DetectedGpu("Intel UHD Graphics 770"),
            new DetectedGpu("NVIDIA GeForce RTX 3080"),
        };
        Assert.Equal(GpuVariant.Cuda, GpuDetector.SuggestVariant(gpus));
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 3090", GpuVendor.Nvidia)]
    [InlineData("AMD Radeon Pro 580", GpuVendor.Amd)]
    [InlineData("Radeon RX 6700", GpuVendor.Amd)]
    [InlineData("Intel HD Graphics 620", GpuVendor.Intel)]
    [InlineData("Unknown GPU XYZ", GpuVendor.Unknown)]
    public void DetectedGpu_VendorClassification(string name, GpuVendor expected)
    {
        var gpu = new DetectedGpu(name);
        Assert.Equal(expected, gpu.Vendor);
    }
}
