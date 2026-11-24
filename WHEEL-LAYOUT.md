# G923 (Xbox One and PC edition) layout, as read by DirectInput

Device: `G923 Racing Wheel for Xbox One and PC`, VID `046D`, PID `C26E`, driving device. 4 axes, 23 buttons, 1 POV hat, force feedback. Captured with `G923Mapper.Probe --dump` on 2026-11-22. All values are raw 0..65535.

| Input | DirectInput field | Rest | Active | Status |
|---|---|---|---|---|
| Wheel | X | ~32054 (not exactly 32768) | 0 = full left, 65535 = full right | Confirmed |
| Accelerator | Y | 65535 | 0 when pressed (inverted) | Confirmed |
| Brake | RZ | 65535 | 0 when pressed (inverted) | Confirmed that it is a pedal and inverted; name inferred from press order |
| Clutch | S0 (Slider 0) | 65535 | 0 when pressed (inverted) | Confirmed (capture 3: 65535 -> 0 held -> back to 65535) |
| Z, RX, RY, S1 | | 0 | no movement | Unused |

Buttons seen so far (1-based DirectInput numbering, as printed by the probe): 5 and 6, almost certainly the paddle shifters (the standard X360 order has LB = 5, RB = 6), but I did not record which physical paddle was which. Face buttons, D-pad/hat and the other wheel buttons have not been exercised yet; the 23-button count means there are plenty more to name in the calibration wizard.

## Gotchas for later milestones

- **Stale initial state.** A freshly acquired device reports 32768 on every axis (RZ, Y, S0, X) until the first real input report arrives, which only happens once something moves. That 32768 is not a real pedal value; with inverted pedals it looks like "half pressed". The mapper must not forward axis values until it has seen a real report, or it should ask the HID layer for the current report instead.
- **Rest value of the wheel varies.** 32054 in one run, 32055/32075 in another. Calibration must record centre rather than assuming 32768.
- **Pedals are inverted.** Calibration should detect direction, as the spec says.

## G923 PlayStation 4 / 5 / PC edition, as calibrated on a real wheel

Device: VID `046D`, PID `C266` (the PlayStation edition's other product id, `C267`, is treated as the same family until one is seen). From a calibration made with Settings, Calibrate on a friend's wheel:

| Input | DirectInput field | Notes |
|---|---|---|
| Wheel | X | signed, centre 32660 of 0..65535 |
| Accelerator | Y | inverted |
| Brake | RZ | inverted |
| Clutch | Slider 0 | inverted |

Buttons (1-based): 1 bottom face button (Cross), 2 left (Square), 3 right (Circle), 4 top (Triangle), 5 and 6 the paddle shifters (5 = paddleR, 6 = paddleL as calibrated), 9 the Share button, 10 Options. L2, R2, L3, R3 and PS were not part of that calibration; they report as button7, button8 and so on until a calibration names them. Calibration names the face buttons the Xbox way (buttonA, buttonB, buttonX, buttonY, view, menu); the app adds the PlayStation names (cross, circle, square, triangle, share, options) for the same numbers and shows those.
