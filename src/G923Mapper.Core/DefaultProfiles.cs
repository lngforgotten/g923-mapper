namespace G923Mapper;

/// <summary>Starter profiles written to the profiles folder on first run. Existing files are never overwritten.</summary>
public static class DefaultProfiles
{
    public const string Default = """
        {
          // Plain 1:1 passthrough: raw linear steering, pedals to triggers, paddles to bumpers.
          "name": "default",
          "device": "auto",
          "safety": "strict",
          "outputs": { "keyboard": false },
          "rules": [
            { "from": "wheel",    "to": "LeftStickX",   "curve": "linear" },
            { "from": "throttle", "to": "RightTrigger", "curve": "linear" },
            { "from": "brake",    "to": "LeftTrigger",  "curve": "linear" },
            { "from": "paddleL",  "to": "Button:LeftShoulder" },
            { "from": "paddleR",  "to": "Button:RightShoulder" },
            { "from": "dpadUp",    "to": "Button:DPadUp" },
            { "from": "dpadDown",  "to": "Button:DPadDown" },
            { "from": "dpadLeft",  "to": "Button:DPadLeft" },
            { "from": "dpadRight", "to": "Button:DPadRight" }
          ]
        }
        """;

    public const string GtaV = """
        {
          // GTA V: wheel travel limited to 540 degrees and a small anti-deadzone because the game
          // adds its own deadzone to the left stick. The 4% deadzone in front of it is needed: without
          // it, any noise or a slightly off centre becomes a constant push of the anti-deadzone size.
          // (This wheel rests up to ~700 counts from the nominal centre, which the 540 degree range turns
          // into about 3.6%.) Setting the centre in the app or calibrating lets you lower it.
          // Clutch acts as a button (e.g. handbrake / lean).
          "name": "GTA V",
          "match": { "exe": ["GTA5.exe", "GTA5_Enhanced.exe"] },
          "device": "auto",
          "safety": "strict",
          "outputs": { "keyboard": false },
          "rules": [
            { "from": "wheel",    "to": "LeftStickX",        "curve": "linear", "deadzone": 0.04, "antiDeadzone": 0.06, "range": 540 },
            { "from": "throttle", "to": "RightTrigger",      "curve": "linear", "invert": false },
            { "from": "brake",    "to": "LeftTrigger",       "curve": "linear" },
            { "from": "clutch",   "to": "Button:RightThumb", "threshold": 0.5, "mode": "hold" },
            { "from": "paddleR",  "to": "Button:RightShoulder" },
            { "from": "paddleL",  "to": "Button:LeftShoulder" }
          ]
        }
        """;

    public static void EnsureWritten(string directory)
    {
        Directory.CreateDirectory(directory);
        WriteIfMissing(Path.Combine(directory, "default.json"), Default);
        WriteIfMissing(Path.Combine(directory, "gta-v.json"), GtaV);
    }

    private static void WriteIfMissing(string path, string content)
    {
        if (!File.Exists(path)) File.WriteAllText(path, content + Environment.NewLine);
    }
}
