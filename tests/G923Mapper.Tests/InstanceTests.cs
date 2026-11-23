using G923Mapper;

namespace G923Mapper.Tests;

public class InstancePlanTests
{
    private static Func<int, bool> Free(params int[] taken) => n => !taken.Contains(n);

    [Fact]
    public void By_default_only_one_copy_runs_and_a_second_launch_gets_nothing()
    {
        Assert.Equal(1, InstancePlan.Choose(false, null, Free()));
        Assert.Null(InstancePlan.Choose(false, null, Free(1)));          // copy 1 is busy: bring it to the front instead
    }

    [Fact]
    public void With_multiple_copies_on_each_launch_takes_the_first_free_number()
    {
        Assert.Equal(1, InstancePlan.Choose(true, null, Free()));
        Assert.Equal(2, InstancePlan.Choose(true, null, Free(1)));
        Assert.Equal(3, InstancePlan.Choose(true, null, Free(1, 2)));
        Assert.Equal(1, InstancePlan.Choose(true, null, Free(2, 3)));    // a gap is reused
    }

    [Fact]
    public void There_is_a_ceiling_so_it_cannot_run_away()
        => Assert.Null(InstancePlan.Choose(true, null, Free(1, 2, 3, 4)));

    [Fact]
    public void An_explicit_instance_is_honoured_only_when_free_and_allowed()
    {
        Assert.Equal(2, InstancePlan.Choose(true, 2, Free()));
        Assert.Null(InstancePlan.Choose(true, 2, Free(2)));              // busy
        Assert.Null(InstancePlan.Choose(false, 2, Free()));              // not allowed while multiple copies are off
        Assert.Equal(1, InstancePlan.Choose(false, 1, Free()));
        Assert.Null(InstancePlan.Choose(true, 0, Free()));
        Assert.Null(InstancePlan.Choose(true, 99, Free()));
    }

    [Fact]
    public void Claiming_stops_at_the_first_success()
    {
        var tried = new List<int>();
        int? n = InstancePlan.Choose(true, null, x => { tried.Add(x); return x == 2; });
        Assert.Equal(2, n);
        Assert.Equal([1, 2], tried);                                      // never claimed 3 or 4
    }

    [Theory]
    [InlineData(1, "settings.json", "g923mapper")]
    [InlineData(2, "settings-2.json", "g923mapper-2")]
    [InlineData(4, "settings-4.json", "g923mapper-4")]
    public void The_first_copy_keeps_its_existing_files_and_the_others_get_their_own(int instance, string settings, string log)
    {
        Assert.Equal(settings, InstancePlan.SettingsFileName(instance));
        Assert.Equal(log, InstancePlan.LogName(instance));
    }

    [Theory]
    [InlineData("--instance 2", 2)]
    [InlineData("--minimized --instance 3", 3)]
    [InlineData("--INSTANCE 2", 2)]
    [InlineData("--instance", null)]
    [InlineData("--instance x", null)]
    [InlineData("--instance 0", null)]
    [InlineData("--minimized", null)]
    public void The_instance_argument_is_read_from_the_command_line(string args, int? expected)
        => Assert.Equal(expected, InstancePlan.ParseArgument(args.Split(' ')));
}

public class WheelPickerTests
{
    private static WheelPicker.Candidate Wheel(string id, string name = "G923 Racing Wheel", int vid = 0x046D, int pid = 0xC26E)
        => new(Guid.Parse(id), vid, pid, name);

    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Func<int, int, bool> Registered = (v, p) => v == 0x046D && p == 0xC26E;

    [Fact]
    public void The_first_copy_gets_the_first_wheel_and_the_second_copy_the_second()
    {
        var wheels = new[] { Wheel("00000000-0000-0000-0000-00000000000b"), Wheel("00000000-0000-0000-0000-00000000000a") };
        Assert.Equal(A, WheelPicker.Pick(wheels, (0x046D, 0xC26E), Registered, 0));
        Assert.Equal(B, WheelPicker.Pick(wheels, (0x046D, 0xC26E), Registered, 1));
    }

    [Fact]
    public void Plug_order_does_not_change_which_wheel_is_which()
    {
        var one = new[] { Wheel("00000000-0000-0000-0000-00000000000a"), Wheel("00000000-0000-0000-0000-00000000000b") };
        var two = one.Reverse().ToArray();
        Assert.Equal(WheelPicker.Pick(one, (0x046D, 0xC26E), Registered, 0), WheelPicker.Pick(two, (0x046D, 0xC26E), Registered, 0));
        Assert.Equal(WheelPicker.Pick(one, (0x046D, 0xC26E), Registered, 1), WheelPicker.Pick(two, (0x046D, 0xC26E), Registered, 1));
    }

    [Fact]
    public void A_copy_with_no_wheel_of_its_own_gets_nothing_and_waits()
    {
        var one = new[] { Wheel("00000000-0000-0000-0000-00000000000a") };
        Assert.NotNull(WheelPicker.Pick(one, (0x046D, 0xC26E), Registered, 0));
        Assert.Null(WheelPicker.Pick(one, (0x046D, 0xC26E), Registered, 1));
        Assert.Null(WheelPicker.Pick([], (0x046D, 0xC26E), Registered, 0));
        Assert.Null(WheelPicker.Pick(one, (0x046D, 0xC26E), Registered, -1));
    }

    [Fact]
    public void Other_controllers_are_never_picked_as_the_wheel()
    {
        var mixed = new[]
        {
            Wheel("00000000-0000-0000-0000-00000000000c", "Controller (XBOX 360 For Windows)", 0x045E, 0x028E),
            Wheel("00000000-0000-0000-0000-00000000000a"),
        };
        Assert.Equal(A, WheelPicker.Pick(mixed, (0x046D, 0xC26E), Registered, 0));
        Assert.Null(WheelPicker.Pick(mixed, (0x046D, 0xC26E), Registered, 1));
    }

    [Fact]
    public void Falls_back_to_registered_ids_then_to_the_name_G923()
    {
        // The preferred ids match nothing (for example a calibrated PlayStation edition): use registered ids.
        var ps = new[] { Wheel("00000000-0000-0000-0000-00000000000a", pid: 0xC267) };
        Assert.Equal(A, WheelPicker.Pick(ps, (0x046D, 0xC26E), (v, p) => p == 0xC267, 0));
        // Nothing registered either: anything with G923 in its name.
        Assert.Equal(A, WheelPicker.Pick(ps, (0x046D, 0xC26E), (_, _) => false, 0));
        // And a differently named device is not taken.
        var other = new[] { Wheel("00000000-0000-0000-0000-00000000000a", "Some Joystick", 0x1234, 0x5678) };
        Assert.Null(WheelPicker.Pick(other, (0x046D, 0xC26E), (_, _) => false, 0));
    }
}
