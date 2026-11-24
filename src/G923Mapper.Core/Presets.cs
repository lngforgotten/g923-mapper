namespace G923Mapper;

public sealed record Preset(string Id, string Title, string Description, string Json)
{
    /// <summary>The title, so lists (and screen readers) show a name rather than the record's fields.</summary>
    public override string ToString() => Title;
}

/// <summary>
/// Starting points for new profiles. They only use inputs every G923 has (wheel, three pedals, the two
/// paddles, the D-pad). Game-specific matching is limited to the one game that was tested.
/// </summary>
public static class Presets
{
    private const string Dpad = """
            { "from": "dpadUp",    "to": "Button:DPadUp" },
            { "from": "dpadDown",  "to": "Button:DPadDown" },
            { "from": "dpadLeft",  "to": "Button:DPadLeft" },
            { "from": "dpadRight", "to": "Button:DPadRight" }
        """;

    public static readonly IReadOnlyList<Preset> All =
    [
        new("passthrough", "Plain wheel",
            "Steering to the left stick, pedals to the triggers, paddles to the bumpers. The safe starting point for most racing games.",
            DefaultProfiles.Default),

        new("gta-v", "GTA V",
            "Steering limited to 540° with a small deadzone and anti-deadzone for GTA's own stick deadzone. Switches on automatically when GTA V starts.",
            DefaultProfiles.GtaV),

        new("racing", "Racing, full lock",
            "Uses the wheel's full rotation for the stick. Clutch is a spare button (X) for handbrake, horn or whatever your game uses.",
            $$"""
            {
              "name": "Racing",
              "device": "auto",
              "safety": "strict",
              "outputs": { "keyboard": false, "pad": "auto" },
              "rules": [
                { "from": "wheel",    "to": "LeftStickX",   "curve": "linear", "deadzone": 0.02 },
                { "from": "throttle", "to": "RightTrigger", "curve": "linear" },
                { "from": "brake",    "to": "LeftTrigger",  "curve": "linear" },
                { "from": "clutch",   "to": "Button:X", "threshold": 0.5 },
                { "from": "paddleL",  "to": "Button:LeftShoulder" },
                { "from": "paddleR",  "to": "Button:RightShoulder" },
            {{Dpad}}
              ]
            }
            """),

        new("soft-steering", "Soft steering",
            "A gentler response around the centre (good for high-speed driving) that still reaches full lock. Small deadzone so a slightly off-centre wheel does not drift.",
            $$"""
            {
              "name": "Soft steering",
              "device": "auto",
              "safety": "strict",
              "outputs": { "keyboard": false, "pad": "auto" },
              "rules": [
                { "from": "wheel",    "to": "LeftStickX",   "curve": "expo", "exponent": 1.6, "deadzone": 0.03 },
                { "from": "throttle", "to": "RightTrigger", "curve": "linear" },
                { "from": "brake",    "to": "LeftTrigger",  "curve": "linear" },
                { "from": "paddleL",  "to": "Button:LeftShoulder" },
                { "from": "paddleR",  "to": "Button:RightShoulder" },
            {{Dpad}}
              ]
            }
            """),

        new("pedals-as-buttons", "Pedals as buttons",
            "For games with no driving: the wheel is the left stick and each pedal presses a button (accelerator A, brake B, clutch X) once pushed halfway.",
            $$"""
            {
              "name": "Pedals as buttons",
              "device": "auto",
              "safety": "strict",
              "outputs": { "keyboard": false, "pad": "auto" },
              "rules": [
                { "from": "wheel",    "to": "LeftStickX", "curve": "linear", "deadzone": 0.03 },
                { "from": "throttle", "to": "Button:A", "threshold": 0.5 },
                { "from": "brake",    "to": "Button:B", "threshold": 0.5 },
                { "from": "clutch",   "to": "Button:X", "threshold": 0.5 },
                { "from": "paddleL",  "to": "Button:LeftShoulder" },
                { "from": "paddleR",  "to": "Button:RightShoulder" },
            {{Dpad}}
              ]
            }
            """),

        new("menus", "Menus: wheel as D-pad",
            "For menus and games with no driving: turning the wheel half way left or right presses D-pad left or right, the accelerator is D-pad up, the brake is D-pad down, and the paddles are A (confirm) and B (back).",
            """
            {
              "name": "Menus",
              "device": "auto",
              "safety": "strict",
              "outputs": { "keyboard": false, "pad": "auto" },
              "rules": [
                { "from": "wheel",    "to": "Button:DPadLeft",  "threshold": -0.5 },
                { "from": "wheel",    "to": "Button:DPadRight", "threshold": 0.5 },
                { "from": "throttle", "to": "Button:DPadUp",    "threshold": 0.5 },
                { "from": "brake",    "to": "Button:DPadDown",  "threshold": 0.5 },
                { "from": "paddleR",  "to": "Button:A" },
                { "from": "paddleL",  "to": "Button:B" }
              ]
            }
            """),

    ];

    /// <summary>
    /// Ready-made profiles for specific games. Only games whose setup has been checked by someone who plays them are listed. Unlike the
    /// templates above they claim their game (so they switch on by themselves) and may use keyboard output, so they are Standard profiles:
    /// not for games with anti-cheat that you have not checked.
    /// </summary>
    public static readonly IReadOnlyList<Preset> Gallery =
    [
        new("game-wardogs-lean", "Game: Wardogs, lean on the pedals",
            "Leans in Wardogs with the pedals instead of the keyboard: the clutch presses Q (lean left) and the accelerator presses E (lean right), a third of the way down. " +
            "Sends key presses, so it is a Standard profile. Switches on by itself when Wardogs starts.",
            """
            {
              "name": "Wardogs lean",
              "match": { "exe": ["WardogsClient-Win64-Shipping.exe"] },
              "device": "auto",
              "safety": "standard",
              "outputs": { "keyboard": true, "pad": "auto" },
              "rules": [
                { "from": "clutch",   "to": "Key:Q", "threshold": 0.35 },
                { "from": "throttle", "to": "Key:E", "threshold": 0.35 }
              ]
            }
            """),
    ];

    /// <summary>
    /// A new profile from a preset, with the given display name. Presets other than the GTA one do not claim any game
    /// (their match list is empty), so a copy never steals auto-switching from the original.
    /// </summary>
    public static Profile? Instantiate(Preset preset, string name, DeviceRegistry devices)
    {
        Profile? p = ProfileLoader.Parse(preset.Json, devices).Profile;
        if (p is null) return null;
        p.Name = name.Trim();
        return p;
    }
}
