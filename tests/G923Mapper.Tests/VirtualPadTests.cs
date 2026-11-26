using G923Mapper;

namespace G923Mapper.Tests;

public class VirtualPadTests
{
    // Real output from this machine, plus a physical controller to prove it is left alone.
    private const string Sample = """
        Microsoft PnP Utility

        Instance ID:                USB\VID_045E&PID_028E\01
        Device Description:         Xbox 360 Controller for Windows
        Class Name:                 XnaComposite
        Class GUID:                 {d61ca365-5af4-4486-998b-9db4734c6ca3}
        Manufacturer Name:          Microsoft
        Status:                     Started
        Driver Name:                xusb22.inf

        Instance ID:                USB\VID_045E&PID_028E\02
        Device Description:         Xbox 360 Controller for Windows
        Class Name:                 XnaComposite
        Manufacturer Name:          Microsoft
        Status:                     Disconnected
        Driver Name:                xusb22.inf

        Instance ID:                USB\VID_045E&PID_028E\5&2D4A1F3&0&2
        Device Description:         Xbox 360 Controller for Windows
        Class Name:                 XnaComposite
        Status:                     Started
        """;

    [Fact]
    public void Lists_virtual_controllers_with_their_state_and_ignores_real_ones()
    {
        var pads = VirtualPadText.Parse(Sample);
        Assert.Equal(2, pads.Count);
        Assert.Equal(@"USB\VID_045E&PID_028E\01", pads[0].InstanceId);
        Assert.True(pads[0].Connected);
        Assert.Equal("Xbox 360 Controller for Windows", pads[0].Description);
        Assert.False(pads[1].Connected);
        Assert.DoesNotContain(pads, p => p.InstanceId.Contains("5&2D4A"));
    }

    [Fact]
    public void Leftovers_are_the_ones_not_connected()
    {
        var left = VirtualPadText.Leftovers(VirtualPadText.Parse(Sample));
        Assert.Single(left);
        Assert.EndsWith(@"\02", left[0].InstanceId);
    }

    [Theory]
    [InlineData(@"USB\VID_045E&PID_028E\01", true)]
    [InlineData(@"usb\vid_045e&pid_028e\0007", true)]
    [InlineData(@"USB\VID_045E&PID_028E\5&2D4A1F3&0&2", false)]   // a real controller
    [InlineData(@"USB\VID_046D&PID_C26E\01", false)]              // not an Xbox pad
    [InlineData(@"USB\VID_045E&PID_028E\01\..", false)]
    [InlineData("", false)]
    public void Only_viGEm_style_ids_count_as_virtual(string id, bool expected) =>
        Assert.Equal(expected, VirtualPadText.IsVirtual(id));

    [Fact]
    public void Unexpected_output_gives_an_empty_list_rather_than_a_crash()
    {
        Assert.Empty(VirtualPadText.Parse(""));
        Assert.Empty(VirtualPadText.Parse("Das ist keine Ausgabe\nGeräte-ID: USB\\VID_045E&PID_028E\\01"));
    }

    [Fact]
    public void The_remove_command_covers_every_id_and_keeps_going_after_a_failure()
    {
        string? args = VirtualPadText.RemoveBatch([@"USB\VID_045E&PID_028E\02", @"USB\VID_045E&PID_028E\03"]);
        Assert.NotNull(args);
        Assert.StartsWith("/c ", args);
        Assert.Contains("pnputil /remove-device \"USB\\VID_045E&PID_028E\\02\" /subtree", args);
        Assert.Contains(" & pnputil /remove-device \"USB\\VID_045E&PID_028E\\03\" /subtree", args);
        Assert.DoesNotContain("&&", args);
    }

    [Fact]
    public void The_remove_command_refuses_nothing_and_anything_that_is_not_virtual()
    {
        Assert.Null(VirtualPadText.RemoveBatch([]));
        Assert.Null(VirtualPadText.RemoveBatch([@"USB\VID_045E&PID_028E\02", @"USB\VID_045E&PID_028E\5&2D4A1F3&0&2"]));
        Assert.Null(VirtualPadText.RemoveBatch([@"HID\VID_046D&PID_C26E&MI_00&Col01\a&1f4abe09&0&0000"]));
    }
}

public class VirtualPadHealthTests
{
    private static IReadOnlyList<HealthItem> Eval(int? connected, int copies = 1, int? leftover = 0) =>
        HealthEvaluator.Evaluate(new HealthFacts
        {
            ViGEmInstalled = true, ViGEmRunning = true, MapperRunning = true, WheelFound = true, WheelReporting = true,
            VirtualPadsConnected = connected, AppCopiesRunning = copies,
            VirtualPadsLeftover = leftover is int n ? Enumerable.Range(2, n).Select(i => $@"USB\VID_045E&PID_028E\{i:00}").ToList() : null,

        });

    [Fact]
    public void One_controller_for_one_copy_is_fine()
    {
        var items = Eval(1);
        Assert.DoesNotContain(items, i => i.Title.Contains("virtual controller") && i.Level >= HealthLevel.Info && !i.Title.Contains("driver"));
        Assert.DoesNotContain(items, i => i.Title.Contains("More virtual"));
    }

    [Fact]
    public void A_second_controller_made_by_another_program_is_a_warning()
    {
        var item = Eval(2).Single(i => i.Title.Contains("More virtual"));
        Assert.Equal(HealthLevel.Warning, item.Level);
        Assert.Contains("x360ce", item.Detail);
    }

    [Fact]
    public void Two_copies_of_the_app_may_have_two_controllers()
    {
        Assert.DoesNotContain(Eval(2, copies: 2), i => i.Title.Contains("More virtual"));
        Assert.Contains(Eval(3, copies: 2), i => i.Title.Contains("More virtual"));
    }

    [Fact]
    public void Leftover_entries_are_a_gentle_tip()
    {
        var item = Eval(1, leftover: 2).Single(i => i.Title.Contains("leftover"));
        Assert.Equal(HealthLevel.Info, item.Level);
        Assert.Contains("2", item.Title);
        Assert.Contains(Eval(1, leftover: 1), i => i.Title == "A leftover virtual controller");
    }

    [Fact]
    public void When_windows_cannot_be_asked_nothing_is_reported()
    {
        var items = Eval(null, leftover: null);
        Assert.DoesNotContain(items, i => i.Title.Contains("More virtual") || i.Title.Contains("leftover"));
    }
}
