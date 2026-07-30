using Avalonia.Controls;

namespace StemForge.Controls;

/// <summary>
/// The YouTube Premium wordmark, shown wherever a source format is premium in StemForge's sense.
/// Shared by the format picker rows and the resolved-URL chips row so the artwork is defined once.
/// </summary>
public partial class YouTubePremiumBadge : UserControl
{
    public YouTubePremiumBadge() => InitializeComponent();
}
