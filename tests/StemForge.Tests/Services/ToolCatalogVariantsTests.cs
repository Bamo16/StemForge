using StemForge.Core.Models;
using StemForge.Core.Services;

namespace StemForge.Tests.Services;

public sealed class ToolCatalogVariantsTests
{
    private static UvToolInstall AudioSeparatorInstall() =>
        Assert.IsType<UvToolInstall>(ToolCatalog.Get(ToolKind.AudioSeparator).InstallStrategy);

    [Fact]
    public void Windows_OffersCudaDirectMLAndCpu()
    {
        var variants = AudioSeparatorInstall().VariantsFor(OSKind.Windows).Select(v => v.Variant);
        Assert.Equal([GpuVariant.Cuda, GpuVariant.DirectML, GpuVariant.Cpu], variants);
    }

    [Fact]
    public void Linux_OffersCudaAndCpuOnly()
    {
        var variants = AudioSeparatorInstall().VariantsFor(OSKind.Linux).Select(v => v.Variant);
        Assert.Equal([GpuVariant.Cuda, GpuVariant.Cpu], variants);
    }

    [Fact]
    public void MacOS_OffersCpuOnly()
    {
        var variants = AudioSeparatorInstall().VariantsFor(OSKind.MacOS).Select(v => v.Variant);
        Assert.Equal([GpuVariant.Cpu], variants);
    }

    [Theory]
    [InlineData(OSKind.Windows)]
    [InlineData(OSKind.Linux)]
    public void CudaVariant_CarriesGpuExtraAndCu121Index(OSKind os)
    {
        var cuda = AudioSeparatorInstall()
            .VariantsFor(os)
            .Single(v => v.Variant == GpuVariant.Cuda);

        Assert.Equal("gpu", cuda.PipExtra);
        Assert.Equal(
            ["--extra-index-url", "https://download.pytorch.org/whl/cu121"],
            cuda.ExtraArgs
        );
    }

    [Theory]
    [InlineData(OSKind.Windows)]
    [InlineData(OSKind.Linux)]
    [InlineData(OSKind.MacOS)]
    public void CpuVariant_UsesCpuExtraWithNoExtraArgs(OSKind os)
    {
        var cpu = AudioSeparatorInstall().VariantsFor(os).Single(v => v.Variant == GpuVariant.Cpu);

        Assert.Equal("cpu", cpu.PipExtra);
        Assert.Empty(cpu.ExtraArgs);
    }

    [Fact]
    public void DirectML_OfferedOnlyOnWindows()
    {
        var install = AudioSeparatorInstall();

        Assert.Contains(install.VariantsFor(OSKind.Windows), v => v.Variant == GpuVariant.DirectML);
        Assert.DoesNotContain(
            install.VariantsFor(OSKind.Linux),
            v => v.Variant == GpuVariant.DirectML
        );
        Assert.DoesNotContain(
            install.VariantsFor(OSKind.MacOS),
            v => v.Variant == GpuVariant.DirectML
        );
    }
}
