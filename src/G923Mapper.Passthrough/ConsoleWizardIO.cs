namespace G923Mapper;

/// <summary>Drives the calibration wizard from the console: each step waits for Enter.</summary>
public sealed class ConsoleWizardIO : IWizardIO
{
    public void Say(string text) => Console.WriteLine(text);

    public bool Step(string prompt, bool allowSkip)
    {
        Console.WriteLine();
        Console.WriteLine(prompt);
        Console.Write(allowSkip ? "  Press Enter to record (or type s then Enter to skip): " : "  Press Enter to record: ");
        string? line = Console.ReadLine();
        if (line is null) throw new EndOfStreamException("Input closed during calibration.");
        if (allowSkip && line.Trim().Equals("s", StringComparison.OrdinalIgnoreCase)) return false;
        Console.WriteLine("  Recording... hold it there until the meter is full (press Enter to use the current position early).");
        return true;
    }

    private string _lastLabel = "";

    public void Progress(WizardProgress p)
    {
        // One live line that rewrites itself: label, a bar for the hold, and what the wizard sees.
        const int width = 20;
        int filled = (int)Math.Round(Math.Clamp(p.Hold, 0, 1) * width);
        string line = $"  {p.Label,-10} now {p.Value,5}  [{new string('#', filled)}{new string('.', width - filled)}] {p.Message}";
        if (Console.IsOutputRedirected)
        {
            if (p.Label != _lastLabel || p.Locked) Console.WriteLine(line);
        }
        else
        {
            int room = Math.Max(20, Console.WindowWidth - 1);
            Console.Write("\r" + (line.Length > room ? line[..room] : line.PadRight(room)));
            if (p.Locked) Console.WriteLine();
        }
        _lastLabel = p.Label;
    }

    public bool AcceptNow()
    {
        // Enter during a measurement means "use where it is now".
        try
        {
            if (Console.IsInputRedirected || !Console.KeyAvailable) return false;
            return Console.ReadKey(intercept: true).Key == ConsoleKey.Enter;
        }
        catch (InvalidOperationException) { return false; }
    }

    public string Ask(string prompt, string defaultValue)
    {
        Console.Write($"{prompt} [{defaultValue}]: ");
        string? line = Console.ReadLine();
        return string.IsNullOrWhiteSpace(line) ? defaultValue : line.Trim();
    }

    public void Pause(int milliseconds) => Thread.Sleep(milliseconds);
}
