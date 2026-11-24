namespace G923Mapper;

/// <summary>The outcome of measuring the wheel for the centring curve (see <see cref="FrictionProbe"/>).</summary>
/// <param name="Ok">The wheel moved, so both numbers are real.</param>
/// <param name="FloorPercent">The least force that turned the wheel, as a percentage of its full force.</param>
/// <param name="Reversed">True when the wheel turns the opposite way to a positive force.</param>
/// <param name="Wheel">The wheel it was measured on, as the app names its USB ids.</param>
/// <param name="Message">What to tell the user.</param>
public sealed record FloorMeasurement(bool Ok, int FloorPercent, bool Reversed, string Wheel, string Message);
