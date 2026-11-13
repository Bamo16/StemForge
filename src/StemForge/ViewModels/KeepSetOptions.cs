using CommunityToolkit.Mvvm.ComponentModel;
using StemForge.Core.Catalog;

namespace StemForge.ViewModels;

/// <summary>One stem the user can keep or discard in the preset form.</summary>
public sealed partial class StemChoice(string name) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    public partial bool IsKept { get; set; } = true;
}

/// <summary>
/// What the preset form can offer for the keep set: the stem names to pick from, or a note saying
/// why it cannot. The picker needs every selected model's stems known with confidence (read from
/// config, or a fixed architecture default); a filename-derived guess is not enough to let a
/// user discard stems on, so those fall back to keep-all with the note. Advisory, never runs a model.
/// </summary>
public sealed record KeepSetOptions(IReadOnlyList<string> Stems, string? Note)
{
    public bool CanPick => Stems.Count > 0;

    public static KeepSetOptions Resolve(
        IEnumerable<(string FriendlyName, ModelProfile? Profile)> selections
    )
    {
        List<string> unsure = [];
        SortedSet<string> names = new(StringComparer.OrdinalIgnoreCase);

        foreach (var (friendlyName, profile) in selections)
        {
            if (profile is not { Confidence: StemSource.Config or StemSource.ArchitectureDefault })
            {
                unsure.Add(friendlyName);
                continue;
            }

            foreach (var stem in profile.Stems)
                names.Add(stem.Name.Trim().ToLowerInvariant());
        }

        return unsure.Count > 0
            ? new KeepSetOptions(
                [],
                "Keeping all stems: couldn't determine the stems of "
                    + string.Join(", ", unsure)
                    + "."
            )
            : new KeepSetOptions([.. names.Where(name => name.Length > 0)], null);
    }
}
