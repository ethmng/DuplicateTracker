namespace DuplicateTracker;

internal static class ByteSize
{
    public static bool TryParse(string text, out long bytes)
    {
        bytes = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        text = text.Trim().Replace(" ", "", StringComparison.Ordinal);
        var index = 0;
        if (text[0] == '+')
            index++;

        var start = index;
        while (index < text.Length && (char.IsDigit(text[index]) || text[index] is '.' or ','))
            index++;

        if (index == start)
            return false;

        var numberPart = text[start..index].Replace(',', '.');
        if (!double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || number < 0)
            return false;

        var suffix = text[index..].Trim().ToUpperInvariant();
        var multiplier = suffix switch
        {
            "" or "B" => 1d,
            "K" or "KB" or "KIB" => 1024d,
            "M" or "MB" or "MIB" => 1024d * 1024d,
            "G" or "GB" or "GIB" => 1024d * 1024d * 1024d,
            "T" or "TB" or "TIB" => 1024d * 1024d * 1024d * 1024d,
            _ => double.NaN
        };

        if (double.IsNaN(multiplier))
            return false;

        var value = number * multiplier;
        if (value > long.MaxValue)
            return false;

        bytes = (long)Math.Round(value);
        return true;
    }

    public static string Format(long bytes)
    {
        if (bytes < 0)
            return "-" + Format(-bytes);

        const double step = 1024d;
        if (bytes < step)
            return bytes.ToString(CultureInfo.InvariantCulture) + " B";

        double value = bytes;
        string[] units = ["KB", "MB", "GB", "TB", "PB"];
        var unit = -1;
        while (value >= step && unit < units.Length - 1)
        {
            value /= step;
            unit++;
        }

        return value.ToString("0.##", CultureInfo.InvariantCulture) + " " + units[unit];
    }
}

internal static class TimeText
{
    public static string Format(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds < 60)
            return elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s";

        if (elapsed.TotalHours < 1)
            return $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";

        return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m";
    }
}

internal static class Keeper
{
    public static string Label(KeepPolicy policy) => policy switch
    {
        KeepPolicy.Oldest => "oldest",
        KeepPolicy.ShortestPath => "shortest path",
        _ => "newest"
    };

    public static DuplicateFile Choose(DuplicateGroup group, KeepPolicy policy)
    {
        if (group.Files.Count == 0)
            throw new InvalidOperationException("The group has no files.");

        return policy switch
        {
            KeepPolicy.Oldest => group.Files
                .OrderBy(file => file.LastWriteTimeUtc)
                .ThenBy(file => file.FullPath.Length)
                .ThenBy(file => file.FullPath, StringComparer.OrdinalIgnoreCase)
                .First(),
            KeepPolicy.ShortestPath => group.Files
                .OrderBy(file => file.FullPath.Length)
                .ThenBy(file => file.FullPath, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(file => file.LastWriteTimeUtc)
                .First(),
            _ => group.Files
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenBy(file => file.FullPath.Length)
                .ThenBy(file => file.FullPath, StringComparer.OrdinalIgnoreCase)
                .First()
        };
    }

    public static DuplicateFile Resolve(
        DuplicateGroup group,
        KeepPolicy policy,
        IReadOnlyDictionary<string, string>? overrides)
    {
        if (overrides is not null
            && overrides.TryGetValue(group.Signature, out var chosen)
            && group.Files.FirstOrDefault(file => PathRules.Same(file.FullPath, chosen)) is { } match)
        {
            return match;
        }

        return Choose(group, policy);
    }
}

internal static class Planner
{
    public static ActionPlan Create(
        IEnumerable<DuplicateGroup> groups,
        KeepPolicy policy,
        IReadOnlyDictionary<string, string>? overrides,
        bool includeHardLinkOnly)
    {
        var planned = new List<PlannedGroup>();
        foreach (var group in groups)
        {
            if (group.IsHardLinkOnly && !includeHardLinkOnly)
                continue;

            var keeper = Keeper.Resolve(group, policy, overrides);
            var extras = group.Files
                .Where(file => !PathRules.Same(file.FullPath, keeper.FullPath))
                .GroupBy(file => PathRules.Normalize(file.FullPath), StringComparer.OrdinalIgnoreCase)
                .Select(paths => paths.First())
                .ToList();

            if (extras.Count == 0)
                continue;

            if (extras.Any(file => PathRules.Same(file.FullPath, keeper.FullPath)))
                throw new InvalidOperationException("Refusing to build a plan that deletes the copy marked to keep.");

            planned.Add(new PlannedGroup
            {
                Source = group,
                Keeper = keeper,
                Extras = extras
            });
        }

        return new ActionPlan { Groups = planned };
    }
}

internal static class ExitCodes
{
    public const int Ok = 0;
    public const int Duplicates = 1;
    public const int Usage = 2;
    public const int ReadErrors = 3;
    public const int ActionFailed = 4;
    public const int Cancelled = 130;

    public static int For(ScanResult result, ActionOutcome? action, bool failOnDuplicates)
    {
        if (action is { Failed: > 0 })
            return ActionFailed;

        if (failOnDuplicates && result.DuplicatesFound)
            return Duplicates;

        if (result.ReadErrors > 0)
            return ReadErrors;

        return Ok;
    }
}

internal static class AppInfo
{
    public const string Name = "DuplicateTracker";

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "1.0.0";
}

internal static class CacheFiles
{
    public static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "thumbs.db",
        "ehthumbs.db",
        "ehthumbs_vista.db",
        "desktop.ini",
        ".ds_store"
    };
}
