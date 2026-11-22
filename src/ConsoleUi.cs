namespace DuplicateTracker;

internal enum Tone
{
    Normal,
    Dim,
    Title,
    Good,
    Warn,
    Bad,
    Accent,
    Strong
}

internal static class ConsoleUi
{
    static readonly object Gate = new();
    static int statusWidth;
    static bool logoShown;

    public static bool ColorEnabled { get; private set; }

    public static void Initialize()
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
        }

        try
        {
            Console.Title = AppInfo.Name;
        }
        catch (IOException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        ColorEnabled = !Console.IsOutputRedirected
            && Environment.GetEnvironmentVariable("NO_COLOR") is null
            && EnableVirtualTerminal();
    }

    public static void Restore()
    {
        ClearStatus();
        try
        {
            Console.CursorVisible = true;
        }
        catch (IOException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    public static int WindowWidth()
    {
        try
        {
            var width = Console.WindowWidth;
            if (width > 0)
                return width;
        }
        catch (IOException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        return 80;
    }

    public static void PrintLogo()
    {
        if (logoShown || Console.IsOutputRedirected)
            return;

        logoShown = true;
        foreach (var line in Ascii.Logo(AppInfo.Version))
            Line(line, Tone.Title);

        Line("");
    }

    public static void Line(string? text = null, Tone tone = Tone.Normal)
    {
        lock (Gate)
            Console.WriteLine(Paint(text ?? "", tone));
    }

    public static void Error(string message)
    {
        lock (Gate)
            Console.Error.WriteLine(message);
    }

    public static void Status(string text)
    {
        if (Console.IsOutputRedirected)
        {
            Line(text, Tone.Dim);
            return;
        }

        var width = Math.Max(20, WindowWidth() - 1);
        if (text.Length >= width)
            text = text[..width];

        lock (Gate)
        {
            var pad = Math.Max(statusWidth, text.Length);
            Console.Write("\r" + text.PadRight(pad));
            statusWidth = text.Length;
        }
    }

    public static void ClearStatus()
    {
        if (Console.IsOutputRedirected || statusWidth == 0)
            return;

        lock (Gate)
        {
            Console.Write("\r" + new string(' ', statusWidth) + "\r");
            statusWidth = 0;
        }
    }

    public static string? ReadLine(string prompt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (Gate)
            Console.Write(prompt);

        var line = Console.ReadLine();
        if (Console.IsInputRedirected)
            Console.WriteLine();

        cancellationToken.ThrowIfCancellationRequested();
        return CleanInput(line);
    }

    public static string? ReadCommand(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (Gate)
            Console.Write("> ");

        if (Console.IsInputRedirected)
        {
            var line = Console.ReadLine();
            Console.WriteLine();
            cancellationToken.ThrowIfCancellationRequested();
            return CleanInput(line);
        }

        try
        {
            var key = Console.ReadKey(intercept: true);
            cancellationToken.ThrowIfCancellationRequested();
            if (key.Key is ConsoleKey.Enter)
            {
                Console.WriteLine();
                return "";
            }

            if (key.Key is ConsoleKey.Escape)
            {
                Console.WriteLine();
                return "q";
            }

            if (key.Key is ConsoleKey.LeftArrow)
            {
                Console.WriteLine();
                return "p";
            }

            if (key.Key is ConsoleKey.RightArrow)
            {
                Console.WriteLine();
                return "n";
            }

            if (char.IsControl(key.KeyChar))
            {
                Console.WriteLine();
                return "";
            }

            Console.Write(key.KeyChar);
            var rest = Console.ReadLine();
            cancellationToken.ThrowIfCancellationRequested();
            return CleanInput(key.KeyChar + (rest ?? ""));
        }
        catch (InvalidOperationException)
        {
            var line = Console.ReadLine();
            Console.WriteLine();
            cancellationToken.ThrowIfCancellationRequested();
            return CleanInput(line);
        }
    }

    public static bool AskYesNo(string prompt, bool defaultYes, CancellationToken cancellationToken)
    {
        var hint = defaultYes ? "[Y/n]" : "[y/N]";
        while (true)
        {
            var answer = ReadLine(prompt + " " + hint + " ", cancellationToken);
            if (string.IsNullOrWhiteSpace(answer))
                return defaultYes;

            if (answer.Equals("y", StringComparison.OrdinalIgnoreCase) || answer.Equals("yes", StringComparison.OrdinalIgnoreCase))
                return true;

            if (answer.Equals("n", StringComparison.OrdinalIgnoreCase) || answer.Equals("no", StringComparison.OrdinalIgnoreCase))
                return false;

            Line("Answer yes or no.", Tone.Warn);
            if (Console.IsInputRedirected)
                return defaultYes;
        }
    }

    public static string? CleanInput(string? raw)
    {
        if (raw is null)
            return null;

        raw = raw.Trim();
        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
            raw = raw[1..^1].Trim();

        return raw;
    }

    public static string Paint(string text, Tone tone)
    {
        if (!ColorEnabled || tone == Tone.Normal)
            return text;

        var code = tone switch
        {
            Tone.Dim => "90",
            Tone.Title => "96",
            Tone.Good => "32",
            Tone.Warn => "33",
            Tone.Bad => "31",
            Tone.Accent => "36",
            Tone.Strong => "1",
            _ => "0"
        };

        return "\u001b[" + code + "m" + text + "\u001b[0m";
    }

    public static void HideCursor()
    {
        try
        {
            Console.CursorVisible = false;
        }
        catch (IOException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    public static void ShowCursor()
    {
        try
        {
            Console.CursorVisible = true;
        }
        catch (IOException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    static bool EnableVirtualTerminal()
    {
        try
        {
            var handle = GetStdHandle(-11);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                return false;

            if (!GetConsoleMode(handle, out var mode))
                return false;

            return SetConsoleMode(handle, mode | 0x0004);
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr GetStdHandle(int handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetConsoleMode(IntPtr handle, out uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetConsoleMode(IntPtr handle, uint mode);
}

internal sealed class ScanProgressDisplay : IProgress<ScanProgress>
{
    readonly bool redirected = Console.IsOutputRedirected;
    long lastTick;
    bool started;

    public void Report(ScanProgress value)
    {
        var now = Environment.TickCount64;
        if (started && now - lastTick < (redirected ? 2000 : 80))
            return;

        lastTick = now;
        if (!started)
        {
            started = true;
            if (!redirected)
                ConsoleUi.Line("");
        }

        ConsoleUi.Status(Render(value));
    }

    public void Complete() => ConsoleUi.ClearStatus();

    static string Render(ScanProgress value)
    {
        var width = ConsoleUi.WindowWidth();
        if (value.Phase == ScanPhase.Walking)
        {
            var text = $"  Walking   {value.FilesSeen.ToString("N0", CultureInfo.InvariantCulture)} files   {ByteSize.Format(value.BytesSeen)}";
            return AppendPath(text, value.CurrentPath, width);
        }

        var total = Math.Max(1, value.CandidateTotal);
        var fraction = Math.Clamp(value.Attempted / (double)total, 0, 1);
        var waitingOnFullRead = value.CandidateTotal > 0 && value.Attempted >= value.CandidateTotal;
        var label = waitingOnFullRead ? "Matching " : "Hashing  ";
        var meter = Ascii.BarMeter(waitingOnFullRead ? 1 : fraction, 22);
        var percent = ((int)Math.Round(fraction * 100)).ToString(CultureInfo.InvariantCulture).PadLeft(3);
        var counts = value.CandidateTotal == 0
            ? "0 files"
            : $"{value.Attempted.ToString("N0", CultureInfo.InvariantCulture)}/{value.CandidateTotal.ToString("N0", CultureInfo.InvariantCulture)}";
        var textLine = $"  {label}{meter} {percent}%   {counts}   {ByteSize.Format(value.BytesRead)} read";
        return AppendPath(textLine, value.CurrentPath, width);
    }

    static string AppendPath(string text, string? path, int width)
    {
        if (string.IsNullOrEmpty(path) || Console.IsOutputRedirected)
            return text;

        var room = width - text.Length - 4;
        if (room < 12)
            return text;

        return text + "   " + Ascii.TrimPath(path, room);
    }
}
