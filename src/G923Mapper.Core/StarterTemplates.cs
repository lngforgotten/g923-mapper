namespace G923Mapper;

/// <summary>
/// The genre templates that come with the app, as ordinary template files in the profile folder (so every one can be edited or deleted).
/// They are written once, the first time the app starts with this feature, and never put back.
/// </summary>
public static class StarterTemplates
{
    /// <summary>File name (without folder) and text of each starter template.</summary>
    public static readonly IReadOnlyList<(string File, string Json)> All =
    [
        ("template-driving.json", """
            {
              "name": "Driving",
              "notes": "Racing and driving games: the wheel steers the left stick 1:1, throttle and brake are the triggers, the paddles are the bumpers. If a game ignores small steering, raise the anti-deadzone on the wheel rule (and keep a small deadzone with it).",
              "isTemplate": true,
              "device": "auto",
              "safety": "strict",
              "outputs": { "keyboard": false },
              "rules": [
                { "from": "wheel", "to": "LeftStickX", "curve": "linear", "deadzone": 0.04 },
                { "from": "throttle", "to": "RightTrigger", "curve": "linear" },
                { "from": "brake", "to": "LeftTrigger", "curve": "linear" },
                { "from": "paddleL", "to": "Button:LeftShoulder", "curve": "linear" },
                { "from": "paddleR", "to": "Button:RightShoulder", "curve": "linear" }
              ]
            }
            """),
        ("template-fps-pedals.json", """
            {
              "name": "FPS, pedal-first",
              "notes": "Shooters played on keyboard and mouse, with the pedals doing the extra actions. The wheel is not mapped, so it is optional. Clutch leans left (Q), throttle leans right (E), brake crouches (C, as a toggle). Each pedal has its own trigger point because G923 pedal travel is long; change a pedal's mode (hold, toggle, pulse) in Edit rules. Uses keys, so a profile made from this needs Standard safety: offline and single-player games only.",
              "isTemplate": true,
              "device": "auto",
              "safety": "standard",
              "outputs": { "keyboard": true },
              "rules": [
                { "from": "clutch", "to": "Key:Q", "curve": "linear", "threshold": 0.35 },
                { "from": "throttle", "to": "Key:E", "curve": "linear", "threshold": 0.5 },
                { "from": "brake", "to": "Key:C", "curve": "linear", "threshold": 0.45, "mode": "toggle" }
              ]
            }
            """),
        ("template-rpg-pedals.json", """
            {
              "name": "RPG / third person, pedal-first",
              "notes": "Third-person and role-playing games on keyboard and mouse, with the pedals doing the combat actions. The wheel is not mapped, so it is optional. Clutch blocks (right mouse button), throttle sprints (Shift), brake dodges (Space, one tap per press). Each pedal has its own trigger point; change a pedal's mode (hold, toggle, pulse) in Edit rules. Uses keys and mouse, so a profile made from this needs Standard safety: offline and single-player games only.",
              "isTemplate": true,
              "device": "auto",
              "safety": "standard",
              "outputs": { "keyboard": true },
              "rules": [
                { "from": "clutch", "to": "Mouse:Right", "curve": "linear", "threshold": 0.4 },
                { "from": "throttle", "to": "Key:Shift", "curve": "linear", "threshold": 0.55 },
                { "from": "brake", "to": "Key:Space", "curve": "linear", "threshold": 0.35, "mode": "pulse" }
              ]
            }
            """),
        ("template-flight.json", """
            {
              "name": "Flight",
              "notes": "A starting point for flight games: the wheel rolls (left stick X) with a gentler curve so small movements stay fine, throttle pitches up and brake pitches down (right stick Y), and the clutch is engine power (right trigger). Change which pedal does what in Edit rules to suit the game.",
              "isTemplate": true,
              "device": "auto",
              "safety": "strict",
              "outputs": { "keyboard": false },
              "rules": [
                { "from": "wheel", "to": "LeftStickX", "curve": "expo", "exponent": 1.6, "deadzone": 0.03 },
                { "from": "throttle", "to": "RightStickY", "curve": "linear", "deadzone": 0.03 },
                { "from": "brake", "to": "RightStickY", "curve": "linear", "deadzone": 0.03, "expr": "0 - x" },
                { "from": "clutch", "to": "RightTrigger", "curve": "linear" }
              ]
            }
            """),
    ];

    /// <summary>Write the starter templates that are not there yet. Returns the file names written. A file that already exists is never touched.</summary>
    public static IReadOnlyList<string> WriteMissing(string profileDir)
    {
        var written = new List<string>();
        Directory.CreateDirectory(profileDir);
        foreach (var (file, json) in All)
        {
            string path = Path.Combine(profileDir, file);
            if (File.Exists(path)) continue;
            File.WriteAllText(path, json.Replace("\r\n", "\n").Replace("\n", Environment.NewLine));
            written.Add(file);
        }
        return written;
    }
}
