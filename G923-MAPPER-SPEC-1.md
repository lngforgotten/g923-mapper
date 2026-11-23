# G923 Mapper: project spec (handoff for Claude Code)

Paste this file into an empty folder on the Windows 11 PC that has the wheel plugged in, then tell Claude Code: "Read G923-MAPPER-SPEC.md and build milestone 1." Work milestone by milestone and test each one on the real hardware before moving on.

## 1. Goal

A Windows 11 tray app that makes a Logitech G923 (Xbox/PC and PlayStation/PC editions) usable in games with no native wheel support, by presenting it to the game as a virtual Xbox 360 controller. Mapping is fully user-customisable, with per-game profiles that load automatically when the game starts.

Target: any XInput game, with one profile per game. Starter profiles: GTA V (story and online) and a generic shooter profile (pedal mapped to lean). Games differ in risk: some online games run kernel anti-cheat, so every profile carries a `safety` mode (see section 8) that controls which output features are allowed.

## 2. Non-goals (v1)

- Real force feedback. An Xbox 360 pad only has rumble. Optional later: translate game rumble into wheel vibration via DirectInput effects.
- Anything that touches a game's process: no DLL injection, no hooking, no memory access, no overlays. See section 8.
- Linux/macOS.
- Macros. x360ce dropped its unfinished macro tab; don't build one.
- An online profile database (privacy and hosting cost). Local import/export only in v1.

## 3. Recommended stack

- C# on .NET 8, WinForms or WPF with a tray icon (NotifyIcon).
- Input: Vortice.DirectInput (SharpDX is unmaintained). Fall back to raw HID if DirectInput hides anything.
- Output: Nefarius.ViGEm.Client (NuGet) talking to the ViGEmBus driver.
- Keyboard/mouse output (optional, off by default): Win32 SendInput.
- Config: JSON profiles in %APPDATA%\G923Mapper\profiles\.
- Game detection: poll running processes every ~2 s by executable name.
- Optional: HidHide, to hide the physical wheel from games so they don't see both devices.

Note: ViGEmBus is retired and archived by its author (Nov 2023), but still works and is used by current x360ce 4.x. Treat it as a dependency that could break with a future Windows update, and keep the output layer behind an interface (`IVirtualPad`) so it can be swapped out.

## 4. Architecture

```
Wheel (DirectInput) -> InputReader -> Calibration -> MappingEngine -> IVirtualPad (ViGEm X360)
                                                          |
                                                          +-> (optional) KeyboardSink (SendInput)
ProfileManager <- ProcessWatcher (game exe -> profile)
TrayUI: device status, active profile, calibration wizard, profile editor, live input monitor
```

### Device handling
- Enumerate game controllers; identify G923 editions by VID/PID. Do not hardcode IDs from memory: log every device's VID/PID/name on first run and have a `devices.json` table that maps known IDs to a device profile (`g923_xbox`, `g923_ps`). The Xbox and PlayStation editions differ in USB IDs and may differ in axis layout and button order.
- Pedals may be inverted or share an axis depending on mode and driver (G HUB present or not). Never assume; the calibration wizard decides.

### Calibration wizard
1. Centre the wheel, record the rest value.
2. Turn fully left, then fully right (record range, and the wheel's rotation, usually 900 degrees).
3. Press each pedal fully, then release (detect axis, direction and range).
4. Press each button when prompted so it can be named.
Store results per device in the profile so the mapper uses calibrated 0..1 / -1..1 values.

### Profile format (JSON)
```json
{
  "name": "GTA V",
  "match": { "exe": ["GTA5.exe", "GTA5_Enhanced.exe"] },
  "device": "g923_xbox",
  "safety": "strict",
  "outputs": { "keyboard": false, "turbo": false },
  "rules": [
    { "from": "wheel",    "to": "LeftStickX",    "curve": "linear", "deadzone": 0.0, "antiDeadzone": 0.06, "range": 540 },
    { "from": "throttle", "to": "RightTrigger",  "curve": "linear", "invert": false },
    { "from": "brake",    "to": "LeftTrigger",   "curve": "linear" },
    { "from": "clutch",   "to": "Button:RightThumb", "threshold": 0.5, "mode": "hold" },
    { "from": "paddleR",  "to": "Button:RightShoulder" },
    { "from": "paddleL",  "to": "Button:LeftShoulder" }
  ]
}
```
Rule fields: `from` (named input), `to` (X360 axis/trigger/button, or `Key:X` if keyboard output is enabled), `curve` (linear, expo, custom points), `deadzone`, `antiDeadzone`, `invert`, `threshold`, `mode` (hold, toggle, pulse), `range` (degrees of wheel travel mapped to full stick).

Steering default must be raw 1:1 linear. Thumbstick-style smoothing feels bad on a wheel. Many games add their own deadzone to the left stick, hence the `antiDeadzone` option.

Custom mapping example (shooter lean): `{ "from": "clutch", "to": "Button:RightThumb", "threshold": 0.4, "mode": "hold" }`. Any axis can drive any button via a threshold, and any button can drive any button, including combos.

### Output limits to respect
- X360 sticks: -32768..32767. Triggers: 0..255. Buttons: A B X Y, LB RB, Back, Start, Guide, LS/RS click, D-pad.
- A game can only be told what an X360 pad can express. For anything else, use keyboard output, and read section 8 first.

## 5. Milestones (test each on the real wheel)

1. **Read the wheel.** Console app lists devices with VID/PID and prints live axis/button values. Acceptance: turning the wheel and pressing pedals visibly changes values; log which axis is which for this wheel.
2. **Virtual pad passthrough.** Hardcoded mapping (wheel to left stick X, pedals to triggers). Acceptance: Windows' "Set up USB game controllers" (joy.cpl) shows an Xbox 360 pad responding correctly; steering is centred and symmetric.
3. **Profile engine.** Load JSON profiles, apply curves/deadzones/rules, hot-reload on file save. Unit-test the mapping maths (no hardware needed).
4. **Calibration wizard.** Produces and saves device calibration. Acceptance: works on the Xbox edition; PlayStation edition support is verified by the friend running the same wizard and sending back the generated `devices.json` entry.
5. **Auto profile switching.** Process watcher activates and deactivates profiles by exe; sensible default profile when nothing matches.
6. **Tray UI and rule editor.** Live input monitor, profile list, a rule editor to add/edit/delete mappings without hand-editing JSON, import/export.
7. **Device hiding (optional).** Integrate HidHide so the physical wheel is hidden from games and visible only to the mapper. Document the manual setup if API integration is awkward.

### Features borrowed from x360ce (milestones 8 to 12)

8. **Health-check tab** (build alongside milestone 2): detect a missing or broken ViGEmBus and guide the install; list and remove leftover virtual controllers; report HidHide status; export an issues report to a file.
9. **Editor quality of life:** click-to-map (click a control, move the input) and "Remap All" stepping through each control; live curve charts for each axis; dead zone and anti-dead zone sliders.
10. **Switching and sharing:** switch profiles on window focus as well as on process start; a hotkey or button combination to toggle emulation on and off; import/export profiles as JSON files so they can be shared (no online database in v1).
11. **Mapping extras:** recentre an axis, axis-to-button, axis-to-D-pad, and a simple expression field for cases like splitting a combined pedal axis.
12. **Later / optional:** wheel centring spring and rumble-to-wheel-vibration (set expectations: Xbox 360 emulation only carries rumble, not true force feedback); a second virtual pad fed by the same wheel; turbo (`standard` profiles only, off by default).

Calibration is where this tool should beat x360ce, which has no general calibration wizard. Keep the wizard (milestone 4) polished.

## 6. Testing notes

- Mapping maths and profile parsing: unit tests, no hardware.
- Everything else needs the wheel plugged in. Claude Code should ask the user to perform physical actions ("turn fully left now") and read the results rather than guessing.
- Test in this order: joy.cpl, then a harmless offline game or GTA V **Story Mode**, and only then GTA Online (see below).
- Check for double input: if the game sees both the real wheel and the virtual pad, steering will fight itself. HidHide or Steam's "disable Steam Input" setting are the usual fixes.

## 7. Packaging

Self-contained `dotnet publish` single-file build, with an installer step that checks for ViGEmBus and tells the user how to install it if missing. Run at user level; request admin only for driver install.

## 8. Safety modes (per profile)

Each profile has `"safety": "strict" | "standard"`.

- `strict`: the default for any game with online play or anti-cheat (e.g. GTA Online). Rules below apply in full.
- `standard`: for offline, single-player or non-anti-cheat games. The user may enable extras: keyboard output, turbo, a second virtual pad. The UI must show a warning when switching a profile from `strict` to `standard`, and must never let a `standard` extra be enabled on a profile whose game exe is on a built-in list of known anti-cheat games (start the list with GTA5.exe / GTA5_Enhanced.exe; the user can edit it).

GTA Online on PC runs BattlEye, a kernel anti-cheat that can ban at hardware level. It is not known whether a ViGEm virtual pad is ever flagged, so reduce risk:

- In every mode, the mapper must only create a virtual controller from outside the game. Never inject, hook, read game memory, or draw overlays on the game process.
- Prefer ViGEm (current x360ce 4.x style) over the old DLL-dropping approach, which has reportedly caused problems with GTA Online.
- Keyboard/mouse synthetic output (SendInput) is more likely to look like automation than a gamepad. Default `outputs.keyboard` to false in the GTA profile and warn the user when enabling it for an online game. (This is a judgement call, not a verified rule.)
- First run in GTA V Story Mode with BattlEye off, then decide with the user whether to try online. Point the user at Rockstar's BattlEye FAQ and support channels.
- In `strict` mode: no macros, turbo, auto-fire or timing assistance of any kind: mapping only, 1 physical input to 1 virtual input.

## 9. Open questions for the user

- Which games besides GTA V need starter profiles?
- Do you want game rumble translated to wheel vibration later?
- Which shooters will use pedal mapping, and do they use pad or keyboard bindings for lean?
