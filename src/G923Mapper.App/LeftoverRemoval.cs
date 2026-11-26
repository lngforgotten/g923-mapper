using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace G923Mapper.App;

/// <summary>Removing leftover virtual controllers, the same from the health tip and from Settings.</summary>
internal static class LeftoverRemoval
{
    /// <summary>
    /// Asks first, then removes (Windows asks for administrator permission). Returns what to tell the user, or null if they said no.
    /// </summary>
    /// <param name="waiting">Called once the user has said yes, before the work starts.</param>
    public static async Task<string?> ConfirmAndRemove(Window owner, IReadOnlyList<string> ids, IgnoredTipsFile ignored, Action waiting)
    {
        if (ThemedMessageBox.Show(owner, Question(ids.Count), "Remove leftovers", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
            != MessageBoxResult.Yes) return null;
        waiting();

        var client = new VirtualPadClient();
        // The list on screen may be some seconds old: look again, so a controller plugged in since is left alone.
        if (await Task.Run(client.List) is not { } before) return "Windows would not list the controllers, so nothing was removed.";
        var targets = VirtualPadText.StillLeftover(ids, before);
        if (targets.Count == 0)
        {
            HealthProbe.SetVirtualPads(before);
            return "There was nothing left to remove.";
        }

        HidHideResult result = await Task.Run(() => client.Remove(targets));
        var after = await Task.Run(client.List);
        if (after is null) HealthProbe.RefreshVirtualPads();
        else HealthProbe.SetVirtualPads(after);
        if (!result.Success) return result.Message;
        if (after is null) return "Windows would not list the controllers again, so the result could not be checked.";

        var remaining = VirtualPadText.StillLeftover(targets, after);
        ignored.Change(t => t.Restore(targets.Except(remaining, StringComparer.OrdinalIgnoreCase)));
        return remaining.Count == 0 ? "Removed." : $"{remaining.Count} could not be removed. Restart Windows and try again.";
    }

    internal static string Question(int count) =>
        $"Remove {count} leftover virtual controller{(count == 1 ? "" : "s")} from Device Manager?\n\n" +
        $"{(count == 1 ? "It is" : "They are")} not plugged in. This cannot be undone, and Windows will ask for administrator permission.";
}
