using G923Mapper;

namespace G923Mapper.Tests;

public class HidHideTests
{
    private const string Json = """
        [
          {
            "friendlyName": "G923 Racing Wheel for Xbox One and PC",
            "devices": [
              { "present": true, "gamingDevice": true, "deviceInstancePath": "HID\\VID_046D&PID_C26E&MI_00\\8&1A2B3C&0&0000", "symbolicLink": "\\\\?\\hid#vid_046d&pid_c26e&mi_00#8&1a2b3c&0&0000#{4d1e55b2}" },
              { "present": true, "gamingDevice": true, "deviceInstancePath": "HID\\VID_046D&PID_C26E&MI_01\\8&1A2B3C&0&0001" }
            ]
          },
          {
            "friendlyName": "Logitech USB Receiver",
            "devices": [ { "present": true, "gamingDevice": false, "deviceInstancePath": "HID\\VID_046D&PID_C547&MI_00\\8&2&0&0000" } ]
          }
        ]
        """;

    [Fact]
    public void Devices_are_read_from_the_JSON_listing()
    {
        var devices = HidHideText.ParseDevices(Json);
        Assert.Equal(3, devices.Count);
        Assert.Equal(@"HID\VID_046D&PID_C26E&MI_00\8&1A2B3C&0&0000", devices[0].InstancePath);
        Assert.Equal("G923 Racing Wheel for Xbox One and PC", devices[0].FriendlyName);
        Assert.Equal("Logitech USB Receiver", devices[2].FriendlyName);
    }

    [Fact]
    public void Every_interface_of_the_wheel_is_found_by_usb_ids_and_nothing_else()
    {
        var wheel = HidHideText.FindDevices(HidHideText.ParseDevices(Json), 0x046D, 0xC26E);
        Assert.Equal(2, wheel.Count);
        Assert.All(wheel, d => Assert.Contains("PID_C26E", d.InstancePath));
        Assert.Empty(HidHideText.FindDevices(HidHideText.ParseDevices(Json), 0x046D, 0x0001));
    }

    [Fact]
    public void Matching_ignores_case()
    {
        var lower = new[] { new HidDeviceInfo("x", @"hid\vid_046d&pid_c26e&mi_00\abc") };
        Assert.Single(HidHideText.FindDevices(lower, 0x046D, 0xC26E));
    }

    [Fact]
    public void Plain_text_output_still_yields_instance_paths()
    {
        const string text = "HID\\VID_046D&PID_C26E&MI_00\\8&1A2B3C&0&0000\r\nHID\\VID_054C&PID_09CC\\7&99&0&0000\r\n";
        var devices = HidHideText.ParseDevices(text);
        Assert.Equal(2, devices.Count);
        Assert.Contains(devices, d => d.InstancePath.Contains("C26E"));
    }

    [Fact]
    public void Duplicates_are_collapsed()
    {
        const string dup = """[ { "devices": [ { "deviceInstancePath": "HID\\VID_1&PID_2" }, { "deviceInstancePath": "hid\\vid_1&pid_2" } ] } ]""";
        Assert.Single(HidHideText.ParseDevices(dup));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json and no paths")]
    [InlineData("[]")]
    [InlineData("{ \"unexpected\": 1 }")]
    public void Garbage_or_empty_output_gives_no_devices_and_never_throws(string output)
        => Assert.Empty(HidHideText.ParseDevices(output));

    [Fact]
    public void The_hidden_list_tells_us_whether_the_wheel_is_hidden()
    {
        var hidden = HidHideText.ParseHiddenList("HID\\VID_046D&PID_C26E&MI_00\\8&1A2B3C&0&0000\nHID\\VID_1234&PID_5678\\x\n");
        Assert.Equal(2, hidden.Count);
        Assert.True(HidHideText.IsHidden(hidden, 0x046D, 0xC26E));
        Assert.False(HidHideText.IsHidden(hidden, 0x046D, 0xC547));
        Assert.False(HidHideText.IsHidden([], 0x046D, 0xC26E));
    }

    [Fact]
    public void Hiding_lets_this_app_see_it_hides_each_interface_and_turns_hiding_on()
    {
        var cmds = HidHideCommands.Hide([@"HID\VID_046D&PID_C26E&MI_00\a", @"HID\VID_046D&PID_C26E&MI_01\b"], @"C:\Apps\G923Mapper.exe");
        Assert.Equal(4, cmds.Count);
        Assert.Equal("--app-reg \"C:\\Apps\\G923Mapper.exe\"", cmds[0]);     // whitelist first, or we would lose the wheel too
        Assert.StartsWith("--dev-hide ", cmds[1]);
        Assert.StartsWith("--dev-hide ", cmds[2]);
        Assert.Equal("--cloak-on", cmds[^1]);
    }

    [Fact]
    public void Unhiding_restores_each_interface_then_removes_the_app_entry()
    {
        var cmds = HidHideCommands.Unhide([@"HID\VID_046D&PID_C26E&MI_00\a"], @"C:\Apps\G923Mapper.exe");
        Assert.Equal(["--dev-unhide \"HID\\VID_046D&PID_C26E&MI_00\\a\"", "--app-unreg \"C:\\Apps\\G923Mapper.exe\""], cmds);
    }

    [Fact]
    public void The_batch_is_one_cmd_line_so_there_is_one_admin_prompt()
    {
        string batch = HidHideCommands.Batch(@"C:\Program Files\HidHide\HidHideCLI.exe", ["--cloak-on", "--app-reg \"x\""]);
        Assert.StartsWith("/c \"", batch);
        Assert.EndsWith("\"", batch);
        Assert.Contains("\"C:\\Program Files\\HidHide\\HidHideCLI.exe\" --cloak-on && \"C:\\Program Files\\HidHide\\HidHideCLI.exe\" --app-reg \"x\"", batch);
    }

    [Fact]
    public void Client_without_the_tool_reports_not_installed_and_fails_politely()
    {
        var client = new HidHideClient(cliPath: null);
        // Locate() finds nothing on a machine without HidHide; on one with it, skip the assertion.
        if (client.IsInstalled) return;
        Assert.Null(client.IsWheelHidden(0x046D, 0xC26E));
        Assert.False(client.Hide(0x046D, 0xC26E, @"C:\x.exe").Success);
        Assert.False(client.Unhide(0x046D, 0xC26E, @"C:\x.exe").Success);
    }
}

/// <summary>
/// A trimmed copy of what HidHideCLI --dev-all printed on a real machine with a G923 (captured by running the tool),
/// so the parser is checked against the real format rather than one I imagined.
/// </summary>
public class HidHideRealOutputTests
{
    private const string Real = """
         [ { "friendlyName" : "Controller (XBOX 360 For Windows)" , "devices" : [
        { "present" : true ,
        "gamingDevice" : true ,
        "symbolicLink" : "\\\\?\\hid#vid_045e&pid_028e&ig_00#3&8968588&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}" ,
        "vendor" : "" ,
        "product" : "Controller (XBOX 360 For Windows)" ,
        "serialNumber" : "" ,
        "usage" : "Gamepad" ,
        "description" : "HID-compliant game controller" ,
        "deviceInstancePath" : "HID\\VID_045E&PID_028E&IG_00\\3&8968588&0&0000" ,
        "baseContainerDeviceInstancePath" : "USB\\VID_045E&PID_028E\\01" ,
        "baseContainerClassGuid" : "{D61CA365-5AF4-4486-998B-9DB4734C6CA3}" ,
        "baseContainerDeviceCount" : 1 } ] } ,
        { "friendlyName" : "Logitech G923 Racing Wheel for Xbox One and PC" , "devices" : [
        { "present" : true , "gamingDevice" : true , "usage" : "Joystick" ,
        "deviceInstancePath" : "HID\\VID_046D&PID_C26E&MI_00&Col01\\a&1f4abe09&0&0000" , "baseContainerDeviceCount" : 4 } ,
        { "present" : true , "gamingDevice" : false , "usage" : "FF43 602" ,
        "deviceInstancePath" : "HID\\VID_046D&PID_C26E&MI_00&Col02\\a&1f4abe09&0&0001" , "baseContainerDeviceCount" : 4 } ,
        { "present" : true , "gamingDevice" : false , "usage" : "FF43 604" ,
        "deviceInstancePath" : "HID\\VID_046D&PID_C26E&MI_00&Col03\\a&1f4abe09&0&0002" , "baseContainerDeviceCount" : 4 } ,
        { "present" : true , "gamingDevice" : false , "usage" : "FFFD FD01" ,
        "deviceInstancePath" : "HID\\VID_046D&PID_C26E&MI_01\\a&c1aa1cb&0&0000" , "baseContainerDeviceCount" : 4 } ] } ]
        """;

    [Fact]
    public void The_real_listing_parses_with_single_backslash_instance_paths()
    {
        var devices = HidHideText.ParseDevices(Real);
        Assert.Equal(5, devices.Count);
        Assert.Contains(devices, d => d.InstancePath == @"HID\VID_046D&PID_C26E&MI_00&Col01\a&1f4abe09&0&0000");
        Assert.All(devices, d => Assert.DoesNotContain(@"\\", d.InstancePath));
    }

    [Fact]
    public void All_four_wheel_interfaces_are_found_but_only_the_gaming_one_is_chosen_for_hiding()
    {
        var devices = HidHideText.ParseDevices(Real);
        Assert.Equal(4, HidHideText.FindDevices(devices, 0x046D, 0xC26E).Count);

        var toHide = HidHideText.FindGaming(devices, 0x046D, 0xC26E);
        Assert.Single(toHide);
        Assert.Contains("MI_00&Col01", toHide[0].InstancePath);
        Assert.True(toHide[0].Gaming);
    }

    [Fact]
    public void Without_gaming_flags_every_matching_interface_is_used_as_a_fallback()
    {
        const string textOnly = "HID\\VID_046D&PID_C26E&MI_00&Col01\\a\r\nHID\\VID_046D&PID_C26E&MI_01\\b\r\n";
        Assert.Equal(2, HidHideText.FindGaming(HidHideText.ParseDevices(textOnly), 0x046D, 0xC26E).Count);
    }

    [Fact]
    public void The_virtual_pad_we_create_is_not_mistaken_for_the_wheel()
        => Assert.Empty(HidHideText.FindGaming(HidHideText.ParseDevices(Real), 0x046D, 0x028E));

    [Fact]
    public void The_hide_commands_for_the_real_wheel_target_one_interface()
    {
        var toHide = HidHideText.FindGaming(HidHideText.ParseDevices(Real), 0x046D, 0xC26E);
        var cmds = HidHideCommands.Hide(toHide.Select(d => d.InstancePath), @"C:\Apps\G923Mapper.exe");
        Assert.Equal(3, cmds.Count);   // app-reg, one dev-hide, cloak-on
        Assert.Equal(1, cmds.Count(c => c.StartsWith("--dev-hide")));
    }

    [Fact]
    public void An_empty_hidden_list_and_the_real_cloak_and_app_outputs_are_handled()
    {
        Assert.Empty(HidHideText.ParseHiddenList(""));      // --dev-list prints nothing when nothing is hidden
        Assert.Empty(HidHideText.ParseHiddenList("--app-reg \"C:\\Program Files\\Nefarius Software Solutions\\HidHide\\x64\\HidHideCLI.exe\""));
        Assert.False(HidHideText.IsHidden([], 0x046D, 0xC26E));
    }
}

public sealed class HidHideClientProcessTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "g923-fakecli-" + Guid.NewGuid());
    public HidHideClientProcessTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string Fake(string body)
    {
        string path = Path.Combine(_dir, "fakecli.cmd");
        File.WriteAllText(path, "@echo off\r\n" + body + "\r\n");
        return path;
    }

    [Fact]
    public void A_tool_that_never_answers_is_given_up_on_instead_of_freezing_the_caller()
    {
        var client = new HidHideClient(Fake("ping -n 30 127.0.0.1 >nul")) { Timeout = TimeSpan.FromSeconds(1) };
        var started = DateTime.UtcNow;
        Assert.Null(client.IsWheelHidden(0x046D, 0xC26E));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(8), "should have given up after about a second");
    }

    [Fact]
    public void A_tool_that_waits_for_keyboard_input_does_not_hang_because_its_input_is_already_closed()
    {
        // `set /p` blocks until a line arrives; with the input closed it returns at once.
        var client = new HidHideClient(Fake("set /p ignored=waiting\r\necho HID\\VID_046D^&PID_C26E^&MI_00\\abc")) { Timeout = TimeSpan.FromSeconds(5) };
        Assert.True(client.IsWheelHidden(0x046D, 0xC26E));
    }

    [Fact]
    public void A_listing_that_names_the_wheel_means_it_is_hidden_and_an_empty_one_means_visible()
    {
        Assert.True(new HidHideClient(Fake("echo --dev-hide HID\\VID_046D^&PID_C26E^&MI_00^&Col01\\a")).IsWheelHidden(0x046D, 0xC26E));
        Assert.False(new HidHideClient(Fake("rem prints nothing, like --dev-list with nothing hidden")).IsWheelHidden(0x046D, 0xC26E));
        Assert.False(new HidHideClient(Fake("echo --dev-hide HID\\VID_1234^&PID_5678\\x")).IsWheelHidden(0x046D, 0xC26E));   // another device
    }

    [Fact]
    public void A_tool_that_fails_is_reported_as_unknown_not_as_visible()
        => Assert.Null(new HidHideClient(Fake("exit /b 3")).IsWheelHidden(0x046D, 0xC26E));
}
