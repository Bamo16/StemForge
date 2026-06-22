namespace StemForge.Core;

public static class SpecialFolderExtensions
{
    public static string GetFolderPath(
        this Environment.SpecialFolder folder,
        params ReadOnlySpan<string> additionalPaths
    ) => Path.Combine([Environment.GetFolderPath(folder), .. additionalPaths]);
}
