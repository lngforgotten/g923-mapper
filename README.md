# G923 Mapper

Makes a Logitech G923 racing wheel (Xbox/PC edition, and PlayStation edition after a quick calibration) work in games as a virtual Xbox 360 or DualShock 4 controller, with profiles, a clickable controller picture, keyboard and mouse output, force feedback settings and more. Windows 10/11.

## Download (the easy way)

1. Go to the [Releases page](https://github.com/lngforgotten/g923-mapper/releases/latest) and download `G923Mapper-win-x64.zip` (about 67 MB, nothing else to install). A smaller `G923Mapper-small-win-x64.zip` also exists, but it needs the .NET 10 Desktop Runtime.
2. Unzip it and double-click **`Setup.cmd`**. It installs the app and offers to install the ViGEmBus driver if it is missing (needed once).
3. Start G923 Mapper from the Start menu. The Setup assistant (More, Setup assistant) walks through the rest, including Calibrate for your wheel.

Windows may show a SmartScreen warning because the app is not code-signed: choose More info, then Run anyway. Source code and tests are all in this repository, under the MIT licence.


See `G923-MAPPER-SPEC.md` for the full spec and `WHEEL-LAYOUT.md` for what was observed on the real wheel.

## Build and install from source

```powershell
.\build\publish.ps1          # builds dist\ : G923Mapper.exe (self-contained, no .NET install needed) plus the setup files
```

Then, from the `dist` folder, **double-click `Setup.cmd`**. It installs the app, offers to install any missing driver, and starts it. To choose options, run it from a terminal:

```powershell
.\Setup.cmd -InstallDrivers -WithHidHide -DesktopShortcut   # everything, no questions asked
.\Install.ps1 -DryRun -WithHidHide                          # show which driver downloads would be used, install nothing
```

`Install.ps1` copies the app to `%LOCALAPPDATA%\Programs\G923Mapper`, adds a Start menu shortcut, and installs what is missing: the **ViGEmBus** driver (required; it creates the virtual controller) and, with `-WithHidHide`, **HidHide** (optional; hides the real wheel from games). It looks up the latest release on GitHub, checks the installer's digital signature, and runs it; Windows asks for administrator permission itself. `-DryRun` shows what would be downloaded without installing. The app also appears in **Windows Settings, Apps, Installed apps**, where Uninstall works. Double-clicking `Uninstall.cmd` (or `Uninstall.ps1`) does the same: it first shows the wheel to games again if HidHide was hiding it (Windows asks permission), then removes the app, its shortcuts and its start-with-Windows entry, and keeps your profiles unless you add `-RemoveData`.

## What it does, in short

- Presents the G923 to games as an Xbox 360 controller, with per-game profiles that switch on by themselves: while the game is running, or (option) only while its window is in front, with Alt-Tab and overlays ignored and a notification when it happens.
- One copy by default; opening it again just brings the open window forward. Optionally (Settings, General) more than one copy can run, each driving its own wheel for two-player setups, with its own controller, settings and log.
- Has its own icon (regenerate with `build\make-icon.ps1`).
- Every wheel button, pedal and D-pad direction can be mapped to any pad output (or a keyboard key); extra devices (handbrake, shifter) can feed the same controller.
- Force feedback: the game's rumble shakes the wheel, with an optional centring spring.
- A **Health** badge in the header says in plain language whether anything needs attention.
- Keyboard output is **off** by default and warns about anti-cheat games (spec section 8).
- Logs go to `%APPDATA%\G923Mapper\logs` (Settings, Open log folder).

### Not yet verified on real hardware

These are built and unit-tested but could not be exercised end to end here: **HidHide**: checked read-only against the real tool (it lists your wheel, the app picks the one gaming interface to hide, and shows the exact command it would run), but the actual hide, which needs a Windows permission prompt and changes what games can see, has not been run, **extra devices** (only one controller was available, so the Add device window was only checked for its empty state), and **keyboard output** (the `SendInput` call is size-checked but was deliberately not sent to a real window). Force feedback was confirmed on the G923 (effects report as playing); how it *feels* is for you to judge.

## Layout

| Project | What it is |
|---|---|
| `src/G923Mapper.Core` | Pure logic, no hardware: device map and normalisation, profile model and validation, shaping maths, mapping engine, hot-reloading profile store, calibration analysis and wizard, `IVirtualPad` |
| `src/G923Mapper.Runtime` | DirectInput wheel reader, ViGEm virtual pad, and `MapperService` (the read-map-write loop with reconnect and hot reload) shared by both front ends |
| `src/G923Mapper.App` | **The GUI**: WPF tray app (builds `G923Mapper.exe`) |
| `src/G923Mapper.Passthrough` | Console front end, including the calibration wizard |
| `src/G923Mapper.Probe` | Milestone 1: lists controllers, live axis/button monitor (`--dump [seconds]` for headless) |
| `tests/G923Mapper.Tests` | xUnit tests for everything in Core (no wheel needed) |

## GUI

```powershell
dotnet run --project src\G923Mapper.App            # or run src\G923Mapper.App\bin\Debug\net8.0-windows\G923Mapper.exe
dotnet run --project src\G923Mapper.App -- --minimized    # start in the tray
```

- Status header (wheel found, VID/PID, active profile, virtual pad) with Start/Stop. Stopping removes the virtual pad.
- **Calibrate…**: the calibration wizard in a window (wake, rest, left/right lock, each pedal, named paddles/buttons, rotation). Saves to `devices.json`; the virtual pad is paused while it runs.
- Profile picker with live validation: errors in red, warnings in amber; a rejected save keeps the previous version running.
- **Switch automatically when a game starts**: while a program listed in a profile's `match.exe` is running, that profile is used; when it closes, your own pick returns. Checked every 2 seconds.
- **Edit rules…**: add, duplicate, delete and edit rules without touching JSON (from/to, curve, deadzone, anti-deadzone, wheel range, invert, button threshold and mode), with a live response-curve preview and checks. **Learn** fills in the input when you press a wheel button, press a pedal or turn the wheel. Saving tidies the file, drops comments and keeps a `.bak`.
- **New… / Import… / Export…** for profiles. Import validates first and keeps the file as it was.
- **Name buttons…**: all of the wheel's buttons in a grid that lights up as you press them; type a name (for example `gearUp`) and it becomes an input name in profiles. Every button also always works as `button1`..`button32`, and every pedal, button and D-pad direction can drive any pad output.
- Live monitor: wheel input (steering in degrees, pedals in %), which wheel buttons are down, and exactly what the game sees on the virtual Xbox 360 pad.
- **Set centre now**: take the wheel's current position as centre (let go of the wheel first).
- **Light / Dark / Auto theme** (the button in the header cycles them; Auto follows the Windows setting live). Uses WPF's built-in Fluent theme, so every control, popup and title bar switches, plus our own palette in `Themes\Light.xaml` and `Themes\Dark.xaml`. The GUI therefore targets .NET 10 (`net10.0-windows`); Core and Runtime stay on .NET 8. Native Windows message boxes and the tray menu stay Windows-styled.
- Closing the window keeps it running in the system tray; right-click the tray icon to Start/Stop or Exit. Only one copy can run (two would create two competing pads).
- If the wheel is unplugged the app waits and reconnects by itself.

## Run (console)

```powershell
cd src\G923Mapper.Passthrough
dotnet run                          # uses the "default" profile
dotnet run -- --profile gta-v       # pick another profile
dotnet run -- --list                # show profiles and any errors, then exit
dotnet run -- --calibrate           # calibration wizard, saves to devices.json
dotnet run -- --centre 33199        # override the wheel centre (raw value)
```

Keys: `C` sets the current wheel position as centre, `Q` quits.

## Xbox and PlayStation wheels

The app is meant to work with the Xbox/PC G923 (built in, USB id 046D:C26E) and the PlayStation 4/5/PC edition. The PlayStation edition's axis and button layout has not been verified on real hardware, so it is **measured, not assumed**: run Settings, Calibrate once (the buttons are then asked for as Cross, Circle, Square, Triangle, Share and Options) and the result is saved under the wheel's own USB ids. Profiles use `"device": "auto"`, which picks the calibrated entry for whichever wheel is connected, so one profile works for both editions. Until a wheel is calibrated the health check says so and the Xbox layout is used as a guess. (From public sources the PlayStation edition reports PID C267 in PlayStation mode and C266 in PC mode.)

## Safety, extras and housekeeping

- **Safety mode.** Each profile has `"safety": "strict"` (the default: controller mapping only, the safe choice for online games and anti-cheat) or `"standard"` (extras allowed). Keyboard output and turbo only work in standard mode and never for a game on the anti-cheat list (`%APPDATA%\G923Mapper\anticheat.txt`, starts with the GTA V executables; Settings, Anti-cheat games…). The rule editor warns before switching a profile to standard.
- **Tabs.** The main window has Home (the controller picture the game sees, with the wheel and pedals), Profiles (create, remap, import, export, edit) and Devices (every game controller Windows sees, hide or show the wheel, and a live picture of the wheel with its numbers). The tray menu can also hide or show the wheel.
- **Earlier versions of profiles.** Whenever a profile file changes (from the editor, an import, or a text editor), the text it had before is kept under `%APPDATA%\G923Mapper\history\<profile>\` (30 per profile). Profiles tab, Earlier versions lists them and restores one; the version it replaces is kept too.
- **Conflict checker.** The health check looks for other software that fights over the controller: x360ce files left in the folder of a game that is running (its `xinput1_3.dll` or `x360ce.ini` replaces the controller system for that game), x360ce, DS4Windows or reWASD running, and Steam (a tip about Steam Input).
- **Smoothing.** A stick or trigger rule can take `"smooth": 80` (milliseconds): the output glides towards the input, taking out jitter and jumps at the price of a little delay. Off by default.
- **Vibration style.** Settings, Force feedback: smooth (a hum, the default), sharp (a buzz) or rough (a rattle). Changing it reconnects the wheel.
- **Second virtual controller.** A profile with `"outputs": { "secondPad": true }` shows the game two controllers; rules whose output starts with `P2:` (`P2:RightTrigger`, `P2:Button:A`) feed the second one, for example the pedals on a controller of their own. The Controller view on Home shows the first controller only.
- **Game gallery.** New profile also lists ready-made profiles for specific games (only ones checked by someone who plays them): currently Wardogs lean, which claims the game and uses keyboard output, so it is a Standard profile.
- **Update check.** Settings, About: press Check for updates to ask GitHub whether a newer release exists (the repository is `lngforgotten/g923-mapper` by default and can be changed). One request, only when you press it; never automatic.
- **Get started checklist.** The first time the app runs it opens a checklist (driver, wheel, calibration, profile, then optional hiding and a test) whose rows tick themselves as you do each step. It is also under More, Setup assistant on the Home tab, and doubles as a quick check that everything is in place.
- **Hide the wheel per profile.** A profile can say `"hideWheel": true` (hide the real wheel from games while its game is running, and give it back when it ends; choosing or remembering the profile by hand never hides anything) or `false` (make sure it is visible); set it under Wheel in the rule editor. Needs HidHide. Each change asks for administrator permission, only happens when the profile becomes active, and a declined prompt is not repeated.
- **Per-profile feel.** A profile can set its own rumble strength, centring spring and rumble motors (`"force": { "rumble": 60, "spring": 20, "motors": "heavy" }`, or Feel in the rule editor), laid over the Settings values while it is in use. Whether force feedback is on at all stays a Settings choice.
- **Auto-centre.** The wheel's centre follows where it comes to rest (still for about 0.7 s, no pedal pressed, within 3 degrees of the calibrated centre), which absorbs the one or two degrees a self-centring wheel is often off. Settings, Wheel. Set centre still works.
- **Tray menu.** Right-click the tray icon to choose a profile, pause or resume the controller, start or stop, or open the app.
- **Axis part.** A rule can use just the positive or negative half of an axis (`"part": "positive"`), stretched to full range, for example to split one combined axis into two controls. Chosen in the rule editor.
- **Devices.** The Devices tab lists every game controller Windows sees (USB ids, which one the app uses, whether it is calibrated) with a live raw reading of the wheel. Test in Windows opens Windows' own controller test.
- **Which rumble to feel.** Settings, Force feedback: play both of a game's rumble motors, only the heavy one, or only the light one.
- **Small download.** `build\publish.ps1` also makes `G923Mapper-small-win-x64.zip` (about 2 MB). It needs the .NET 10 Desktop Runtime installed; the larger zip includes everything.
- **Click the controller to assign.** On Home and on the Devices tab the controller picture is clickable. Hover a control to see what feeds it; click it, press Record, then press the wheel button or pedal (or turn the wheel) you want. It is saved to the running profile at once. A stick has three slots (left-right, up-down, click), and Clear removes a mapping.
- **Show mappings.** Tick "Show mappings" above the controller picture (Home or Devices) to tag every mapped control with the wheel control that feeds it. The choice is remembered.
- **Keys and mouse strip.** Under the controller picture (Home and Devices) a "Keys and mouse" strip lists every wheel control that presses a key or a mouse button (`paddleR -> Q`), lit while you use it. Click a chip to change it (Record the wheel control, type or press the key, or pick a mouse button) or remove it; the "+" chip adds one. If the profile is strict, saving asks before turning keys on (the profile becomes Standard); for a game on the anti-cheat list it refuses, and the strip says when saved keys will not be sent.
- **Signal-flow lines.** The Devices tab draws chips for the wheel controls the profile uses, down each side of the controller, each with a line to the control it feeds. Point at a chip or a control to see just its lines. A line lights up while you use that wheel control.
- **Live dot on the response curve.** In the rule editor, moving the wheel or a pedal moves a dot along the rule's curve, with guide lines to both axes, so deadzone and curve changes can be judged by feel. (Not shown for rules that use an expression.)
- **Rumble meter and history.** The Devices tab shows what the game is asking of the heavy and light rumble motors: two level meters and a graph of the last ten seconds.
- **Pop-out view.** More, Pop-out view (or the tray menu) opens a small always-on-top window with the controller picture, steering and the three pedals.
- **High-contrast theme.** Settings, Appearance, High contrast: black and white with a gold accent. Auto also switches to it when Windows' own high contrast mode is on.
- **Remap all.** More, Remap all walks through every control of the virtual controller (steering, triggers, face buttons, bumpers, D-pad...) and asks you to press the wheel button or pedal for each; skip what you do not need. It builds a new strict profile from your answers. For a DualShock 4 profile the steps are named the PlayStation way.
- **Wheel button combinations.** Settings, While playing: hold two or more wheel buttons together for about a second to pause or resume the controller, or to switch to the next or previous profile. Off by default; use buttons your profile does not use.
- **Controller view.** The main window has a "Controller view" picture of the controller the game sees; it lights up as you use the wheel and pedals.
- **Axis as D-pad.** A rule from the wheel or a pedal to `Button:DPadLeft` (threshold -50%) and so on already works; the "Menus: wheel as D-pad" preset sets it up.
- **DualShock 4 output.** A profile can present the game with a DualShock 4 instead of the Xbox 360 controller (`"outputs": { "pad": "ds4" }`, or Controller in the rule editor). Rules keep the Xbox names and are translated: A = Cross, B = Circle, X = Square, Y = Triangle, shoulders = L1/R1, triggers = L2/R2, Back = Share, Start = Options, Guide = PS button. Changing it reconnects the virtual controller.
- **Draggable curves.** Set a rule's curve to `custom` and drag the points on the response graph: click to add one, drag to move, right-click to remove. The points text box follows.
- **Keys and mouse buttons.** A rule's output can be any keyboard key (`Key:Q`, `Key:Semicolon`, `Key:F13`, `Key:NumpadAdd`, ... the rule editor lists them all) or a mouse button (`Mouse:Left`, `Mouse:Right`, `Mouse:Middle`, `Mouse:X1`, `Mouse:X2`). They follow the same rules as keyboard output: standard safety, "Allow keyboard and mouse output" on, and never for a game on the anti-cheat list. **The trigger point** for a pedal or axis driving a button, key or mouse button is "Press at %" in the rule editor (`"threshold": 0.35` in the file): 35 means pressed once the pedal is 35% down.
- **Percentages in the rule editor.** Deadzone, anti-deadzone and "Press at" are typed as percentages (4 or 4%). Profile files still store them as fractions (`0.04`).
- **Expression field.** A rule can take `"expr": "max(0, x)"`: `x` is the rule's input, other names are other inputs of the device. Useful for splitting a combined pedal axis (`max(0, x)` for one half, `max(0, -x)` for the other). Operators `+ - * /`, brackets, and `min max abs clamp sqrt pow`. Applied after range, before invert and the curve.
- **Turbo.** A button rule with `"turbo": true` (and `"outputs": { "turbo": true }`, standard safety) presses and releases repeatedly while held, `"turboHz"` times a second (default 10). Off by default.
- **Pause shortcut.** Settings, While playing: an optional global shortcut (for example Ctrl+Alt+F10) that pauses and resumes the controller; while paused it stays connected but every control is released. Off by default.
- **Release when the game is not in front.** Optional (Settings, While playing): while a profile's game is running but another window is in front, every control is released, so a held pedal cannot keep driving the game in the background.
- **Virtual controllers.** Settings lists every virtual Xbox 360 controller on the PC, flags extra ones made by other programs, and removes leftover entries from earlier sessions (one administrator prompt; real controllers are never touched). The health check reports both.
- **The wheel and HidHide.** By default the app shows the wheel again when it closes (More, Exit G923 Mapper; closing the window only hides it to the tray) and warns at start if it was left hidden.

## Calibration (milestone 4)

`dotnet run -- --calibrate` walks through: wake the wheel, rest, full left, full right, each pedal (clutch skippable), named buttons (all skippable), rotation degrees. Rest, each lock and each pedal are measured live: after you press Record, a meter shows where the control is and how far through a 1.2 second hold you are, and only a position held steady and well away from rest locks in (a pause part way cannot). It also shows how far along the axis the stop is; if you have limited the angle in G HUB, turn only to that stop. "Use this position" (Enter in the console) accepts the current spot early. It detects the axis, direction and range of each control rather than assuming them, and saves the result to `%APPDATA%\G923Mapper\devices.json` under the name you give (default `g923_xbox`; use `g923_ps` for the PlayStation edition). It also prints the entry: **a PlayStation-edition owner should send that JSON back** to add a built-in entry.

A saved entry replaces the built-in one of the same name. Profiles can only use input names the calibrated device defines, so if you skip the paddles, rules using `paddleL`/`paddleR` will be rejected with a clear message.

The wheel's resting position drifts between sessions (observed 32054 to 33425), so a calibrated centre can be slightly off on another day; press `C` with the wheel at rest to re-centre.

## Test

```powershell
dotnet test tests\G923Mapper.Tests
```

## Profiles

Profiles live in `%APPDATA%\G923Mapper\profiles\*.json`; starter `default.json` and `gta-v.json` are written on first run and never overwritten. Save a file while the app runs and it reloads. A file with errors is rejected and the previous version keeps running; the errors are printed.

Rule fields: `from`, `to`, `curve` (`linear`/`expo`/`custom`), `exponent` (expo), `points` (custom, `[[x,y],...]`), `deadzone`, `antiDeadzone`, `invert`, `threshold` (negative = at or below), `mode` (`hold`/`toggle`/`pulse`), `pulseMs`, `range` (wheel degrees that map to full stick).

Processing order: invert, range, deadzone, curve, anti-deadzone. Several rules on one axis add (clamped); several on one button OR together.

Inputs (`from`): `wheel`, `throttle`, `brake`, `clutch`, `paddleL`, `paddleR`, `button1`..`button32`, `dpadUp/Down/Left/Right`.
Targets (`to`): `LeftStickX/Y`, `RightStickX/Y`, `LeftTrigger`, `RightTrigger`, `Button:<A|B|X|Y|LeftShoulder|RightShoulder|Back|Start|Guide|LeftThumb|RightThumb|DPadUp|DPadDown|DPadLeft|DPadRight>`. `Key:<name>` is accepted but does nothing yet (keyboard sink is later).

Optional `%APPDATA%\G923Mapper\devices.json` adds or overrides device maps (needed for the PlayStation edition).
