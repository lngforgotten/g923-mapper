namespace G923Mapper;

/// <summary>The outcome of measuring the wheel for the centring curve (see <see cref="FrictionProbe"/>).</summary>
/// <param name="Ok">The wheel moved, so both numbers are real.</param>
/// <param name="FloorPercent">The least force that turned the wheel, as a percentage of its full force.</param>
/// <param name="Reversed">True when the wheel turns the opposite way to a positive force.</param>
/// <param name="Wheel">The wheel it was measured on, as the app names its USB ids.</param>
/// <param name="Message">What to tell the user.</param>
public sealed record FloorMeasurement(bool Ok, int FloorPercent, bool Reversed, string Wheel, string Message);

/// <summary>The outcome of a recording of the wheel (see <see cref="MapperService.RecordTrace"/>).</summary>
/// <param name="Ok">A recording was made.</param>
/// <param name="Path">The CSV it was saved to, or empty if it could not be saved.</param>
/// <param name="Summary">The numbers behind the verdict.</param>
/// <param name="Message">What it shows, in a sentence.</param>
public sealed record TraceResult(bool Ok, string Path, TraceSummary? Summary, string Message);
