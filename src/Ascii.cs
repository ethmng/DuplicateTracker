namespace DuplicateTracker;

internal static class Ascii
{
    public static IEnumerable<string> Logo(string version) => LogoFor(ConsoleUi.WindowWidth(), version);

    public static IEnumerable<string> LogoFor(int windowWidth, string version)
    {
        var inner = windowWidth >= 72 ? 64 : 46;
        return Card(inner, LogoRows(inner, version));
    }

    public static IEnumerable<string> Card(int inner, IEnumerable<string> rows)
    {
        yield return Bar(inner);
        foreach (var row in rows)
            yield return "| " + Fit(row, inner) + " |";

        yield return Bar(inner);
    }

    public static string Bar(int inner) => "+" + new string('-', inner + 2) + "+";

    public static string Section(string title, int width)
    {
        var label = " " + title + " ";
        if (label.Length >= width)
            return label;

        return label + new string('-', width - label.Length);
    }

    public static string BarMeter(double fraction, int width)
    {
        width = Math.Max(4, width);
        fraction = Math.Clamp(fraction, 0, 1);
        var inner = width - 2;
        var filled = (int)Math.Round(fraction * inner);
        return "[" + new string('#', filled) + new string('-', Math.Max(0, inner - filled)) + "]";
    }

    public static string Fit(string text, int width)
    {
        if (width <= 0)
            return "";

        if (text.Length <= width)
            return text.PadRight(width);

        if (width <= 3)
            return text[..width];

        return text[..(width - 1)] + ".";
    }

    public static string TrimPath(string path, int width)
    {
        if (width <= 0)
            return "";

        if (path.Length <= width)
            return path;

        if (width <= 3)
            return path[^width..];

        return "..." + path[^(width - 3)..];
    }

    static IEnumerable<string> LogoRows(int inner, string version)
    {
        if (inner < 58)
        {
            yield return Align(inner, "DuplicateTracker", version);
            yield return "Identical files, matched by SHA-256";
            yield return "Keep one copy.";
            yield break;
        }

        string[] mark =
        [
            "######  ######",
            "##  ##    ##  ",
            "##  ##    ##  ",
            "######    ##  "
        ];
        string[] text =
        [
            "DuplicateTracker",
            "Identical files, matched by SHA-256",
            version,
            "Keep one copy."
        ];

        yield return "";
        for (var row = 0; row < mark.Length; row++)
            yield return "  " + mark[row] + "   " + text[row];

        yield return "";
    }

    static string Align(int inner, string left, string right)
    {
        if (left.Length + right.Length >= inner)
            return Fit(left + " " + right, inner);

        return left + new string(' ', inner - left.Length - right.Length) + right;
    }
}
