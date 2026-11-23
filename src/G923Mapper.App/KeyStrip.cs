using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace G923Mapper.App;

/// <summary>
/// The "Keys and mouse" strip under the controller picture: one chip for each wheel control that presses a key or a mouse button
/// ("paddleR  →  Q"), and a "+" chip to add one. A chip lights up while its wheel control is being used.
/// </summary>
public sealed class KeyStrip : WrapPanel
{
    private readonly List<(Button Chip, KeyMapping Map, bool Lit)> _chips = new();

    /// <summary>A chip was clicked (its rule), or the "+" chip (null).</summary>
    public event Action<Rule?>? ChipClicked;

    private Button MakeChip(string text, string tip)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 6), FontSize = 11.5, ToolTip = tip };
        b.SetResourceReference(StyleProperty, "Secondary");
        return b;
    }

    /// <summary>A line to show under the strip when the profile has key or mouse rules that will not be sent, with the reason; empty otherwise.</summary>
    public static string NoteFor(Profile? profile)
    {
        if (profile is null || KeyMappings.Of(profile).Count == 0) return "";
        KeyOutputStatus status = KeyMappings.Status(profile);
        return status.State == KeyOutput.Active ? "" : $"These do nothing right now: {status.Reason}.";
    }

    /// <summary>Rebuild the chips for these mappings.</summary>
    public void Set(IReadOnlyList<KeyMapping> maps)
    {
        Children.Clear();
        _chips.Clear();
        foreach (KeyMapping m in maps)
        {
            Button chip = MakeChip($"{m.Rule.From}  →  {m.Key}", $"{m.Rule.From} presses {m.Key}. Click to change or remove it.");
            Rule rule = m.Rule;
            chip.Click += (_, _) => ChipClicked?.Invoke(rule);
            Children.Add(chip);
            _chips.Add((chip, m, false));
        }
        Button add = MakeChip("+  Key or mouse", "Make a wheel button or pedal press a key or a mouse button.");
        add.Click += (_, _) => ChipClicked?.Invoke(null);
        Children.Add(add);
    }

    /// <summary>Light the chips whose wheel control is in use. <paramref name="level"/> gives how far each control is pressed (0 to 1).</summary>
    public void Update(Func<string, double> level)
    {
        for (int i = 0; i < _chips.Count; i++)
        {
            var (chip, map, lit) = _chips[i];
            bool now = Math.Abs(level(map.Rule.From)) > 0.04;
            if (now == lit) continue;
            _chips[i] = (chip, map, now);
            if (now)
            {
                chip.SetResourceReference(Control.BackgroundProperty, "Accent");
                chip.SetResourceReference(Control.ForegroundProperty, "OnAccent");
            }
            else
            {
                chip.SetResourceReference(Control.BackgroundProperty, "SecBack");
                chip.SetResourceReference(Control.ForegroundProperty, "TextPrimary");
            }
        }
    }
}
