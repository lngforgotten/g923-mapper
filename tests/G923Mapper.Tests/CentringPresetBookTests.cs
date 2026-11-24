using G923Mapper;

namespace G923Mapper.Tests;

public class CentringPresetBookTests
{
    private static ForceSettings Current(int strength = 33) => new() { SpringPercent = strength, CentreDegrees = 9, PeakDegrees = 70, LighteningPercent = 55, DampingPercent = 7 };

    [Fact]
    public void Every_built_in_preset_fits_the_sliders_and_stays_inside_the_pull_limit()
    {
        foreach (var p in CentringCurve.Presets)
        {
            Assert.InRange(p.CentreDegrees, 1, 45);
            Assert.InRange(p.PeakDegrees, 10, 180);
            Assert.True(p.PeakDegrees > p.CentreDegrees, p.Name);
            Assert.InRange(p.LighteningPercent, 0, 100);
            Assert.InRange(p.DampingPercent, 0, 100);

            var s = new ForceSettings { CentringStyle = CentringCurve.StyleCurve, FloorPercent = 0 };
            p.ApplyTo(s);
            double previous = CentringCurve.Force(1.0, 0, s, 450), worst = 0;
            for (double a = 2; a <= 200; a++)
            {
                double now = CentringCurve.Force(a, 0, s, 450);
                worst = Math.Max(worst, Math.Abs(now - previous));
                previous = now;
            }
            Assert.True(worst <= CentringCurve.MaxPullPerDegree * 1.05, $"{p.Name}: {worst} per degree");
        }
    }

    [Fact]
    public void The_off_preset_really_has_no_pull_and_the_others_do()
    {
        var off = CentringCurve.Presets.Single(p => p.Name.StartsWith("Off"));
        var s = new ForceSettings { CentringStyle = CentringCurve.StyleCurve };
        off.ApplyTo(s);
        Assert.Equal(0, CentringCurve.Force(40, 0, s, 450));
        foreach (var p in CentringCurve.Presets.Where(p => p != off))
        {
            p.ApplyTo(s);
            Assert.NotEqual(0, CentringCurve.Force(40, 0, s, 450));
        }
    }

    [Fact]
    public void A_name_is_checked_before_a_preset_is_saved()
    {
        Assert.Null(CentringPresetBook.NameProblem("Wardogs tank"));
        Assert.NotNull(CentringPresetBook.NameProblem(""));
        Assert.NotNull(CentringPresetBook.NameProblem("   "));
        Assert.NotNull(CentringPresetBook.NameProblem(new string('x', CentringPresetBook.MaxNameLength + 1)));
        Assert.NotNull(CentringPresetBook.NameProblem("family CAR"));                  // a built-in name, whatever the case
        Assert.NotNull(CentringPresetBook.NameProblem(null));
    }

    [Fact]
    public void The_current_settings_become_a_preset_that_can_always_be_applied()
    {
        var p = CentringPresetBook.FromSettings("  Mine ", Current());
        Assert.Equal("Mine", p.Name);
        var s = new ForceSettings();
        p.ApplyTo(s);
        Assert.True(p.Matches(s));
        Assert.Equal(70, s.PeakDegrees);

        var wild = CentringPresetBook.FromSettings("w", new ForceSettings { SpringPercent = 900, CentreDegrees = -4, PeakDegrees = 5000, LighteningPercent = -1, DampingPercent = 400 });
        Assert.InRange(wild.Strength, 0, 100);
        Assert.InRange(wild.CentreDegrees, 1, 45);
        Assert.InRange(wild.PeakDegrees, 10, 180);
        Assert.True(wild.PeakDegrees > wild.CentreDegrees);
        Assert.InRange(wild.LighteningPercent, 0, 100);
        Assert.InRange(wild.DampingPercent, 0, 100);
    }

    [Fact]
    public void Saving_adds_a_preset_and_saving_the_same_name_again_replaces_it()
    {
        var mine = new List<CentringPreset>();
        Assert.True(CentringPresetBook.Save(mine, CentringPresetBook.FromSettings("Wardogs", Current(20))));
        Assert.True(CentringPresetBook.Save(mine, CentringPresetBook.FromSettings("Assetto", Current(30))));
        Assert.True(CentringPresetBook.Save(mine, CentringPresetBook.FromSettings("wardogs", Current(50))));     // same name, other case
        Assert.Equal(2, mine.Count);
        Assert.Equal(50, mine[0].Strength);
        Assert.Equal("wardogs", mine[0].Name);
        Assert.Equal("Assetto", mine[1].Name);
    }

    [Fact]
    public void A_built_in_name_or_a_full_list_is_refused_without_changing_anything()
    {
        var mine = new List<CentringPreset>();
        Assert.False(CentringPresetBook.Save(mine, CentringPresetBook.FromSettings("Family car", Current())));
        Assert.Empty(mine);
        for (int i = 0; i < CentringPresetBook.MaxOwnPresets; i++) Assert.True(CentringPresetBook.Save(mine, CentringPresetBook.FromSettings("p" + i, Current())));
        Assert.False(CentringPresetBook.Save(mine, CentringPresetBook.FromSettings("one too many", Current())));
        Assert.Equal(CentringPresetBook.MaxOwnPresets, mine.Count);
        Assert.True(CentringPresetBook.Save(mine, CentringPresetBook.FromSettings("p3", Current(77))));          // replacing still works when full
    }

    [Fact]
    public void Only_the_persons_own_presets_can_be_deleted_and_the_built_in_ones_are_never_in_that_list()
    {
        var mine = new List<CentringPreset> { CentringPresetBook.FromSettings("Mine", Current()) };
        Assert.True(CentringPresetBook.IsMine(mine, "mine"));
        Assert.False(CentringPresetBook.IsMine(mine, "Family car"));
        Assert.False(CentringPresetBook.Remove(mine, "Family car"));
        Assert.True(CentringPresetBook.Remove(mine, "MINE"));
        Assert.Empty(mine);
        Assert.False(CentringPresetBook.Remove(mine, "Mine"));
    }

    [Fact]
    public void The_whole_list_is_the_built_in_ones_then_the_persons_own()
    {
        var mine = new List<CentringPreset> { CentringPresetBook.FromSettings("Mine", Current()) };
        var all = CentringPresetBook.All(mine);
        Assert.Equal(CentringCurve.Presets.Count + 1, all.Count);
        Assert.Equal("Mine", all[^1].Name);
        Assert.Equal(CentringCurve.Presets.Count, CentringPresetBook.All(null).Count);
    }

    [Fact]
    public void Saved_presets_survive_the_settings_file()
    {
        var mine = new List<CentringPreset> { CentringPresetBook.FromSettings("Wardogs tank", Current(42)) };
        string json = System.Text.Json.JsonSerializer.Serialize(mine);
        var back = System.Text.Json.JsonSerializer.Deserialize<List<CentringPreset>>(json)!;
        Assert.Equal(mine[0], back[0]);
    }
}
