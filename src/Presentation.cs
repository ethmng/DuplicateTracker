namespace DuplicateTracker;

internal static class SettingsText
{
    public static string Brief(ScanSettings settings)
    {
        var parts = new List<string>
        {
            "min " + ByteSize.Format(settings.MinSize),
            settings.Recurse ? "subfolders" : "this folder only",
            settings.IncludeEmpty ? "include empty" : "skip empty",
            settings.SkipCacheFiles ? "skip cache files" : "include cache files",
            settings.IncludeCloud ? "include cloud-only" : "skip cloud-only"
        };

        if (!settings.FollowLinks)
            parts.Add("skip folder links");

        if (settings.Extensions.Count > 0)
            parts.Add("ext " + string.Join(',', settings.Extensions.OrderBy(item => item, StringComparer.OrdinalIgnoreCase)));

        if (settings.ExcludeExtensions.Count > 0)
            parts.Add("not " + string.Join(',', settings.ExcludeExtensions.OrderBy(item => item, StringComparer.OrdinalIgnoreCase)));

        if (settings.ExcludeDirectories.Count > 0)
            parts.Add("skip dirs " + string.Join(',', settings.ExcludeDirectories.OrderBy(item => item, StringComparer.OrdinalIgnoreCase)));

        return string.Join(" | ", parts);
    }
}

internal static class Presentation
{
    public static void PrintSummary(ScanResult result)
    {
        var width = Math.Clamp(ConsoleUi.WindowWidth() - 2, 40, 78);
        ConsoleUi.Line("");
        ConsoleUi.Line(Ascii.Section("Scan", width), Tone.Dim);
        Stat("Finished", TimeText.Format(TimeSpan.FromMilliseconds(result.ElapsedMilliseconds)));
        Stat("Files walked", result.FilesSeen.ToString("N0", CultureInfo.InvariantCulture));
        Stat("Data walked", ByteSize.Format(result.BytesSeen));
        Stat("Unique size, not opened", result.UniqueSize.ToString("N0", CultureInfo.InvariantCulture));
        Stat("Opened and hashed", result.FilesHashed.ToString("N0", CultureInfo.InvariantCulture));
        if (result.SkippedEmpty > 0)
            Stat("Skipped empty files", result.SkippedEmpty.ToString("N0", CultureInfo.InvariantCulture));
        if (result.SkippedSmall > 0)
            Stat("Skipped, too small", result.SkippedSmall.ToString("N0", CultureInfo.InvariantCulture));
        if (result.SkippedCache > 0)
            Stat("Skipped cache files", result.SkippedCache.ToString("N0", CultureInfo.InvariantCulture));
        if (result.SkippedCloud > 0)
            Stat("Skipped cloud-only", result.SkippedCloud.ToString("N0", CultureInfo.InvariantCulture));
        if (result.SkippedExtension > 0)
            Stat("Skipped by extension", result.SkippedExtension.ToString("N0", CultureInfo.InvariantCulture));
        if (result.SkippedReparse > 0)
            Stat("Skipped folder links", result.SkippedReparse.ToString("N0", CultureInfo.InvariantCulture));
        if (result.SkippedOutsideLink > 0)
            Stat("Skipped links outside", result.SkippedOutsideLink.ToString("N0", CultureInfo.InvariantCulture));
        if (result.ReadErrors > 0)
            Stat("Not readable", result.ReadErrors.ToString("N0", CultureInfo.InvariantCulture), Tone.Warn);

        var space = result.SpaceGroups.ToList();
        var links = result.HardLinkGroups.ToList();
        ConsoleUi.Line(Ascii.Section("Duplicates", width), Tone.Dim);
        if (space.Count == 0 && links.Count == 0)
        {
            ConsoleUi.Line("  No duplicates.");
            ConsoleUi.Line("  Files that share a size were hashed. None had the same signature.");
            return;
        }

        Stat("Duplicate groups", space.Count.ToString("N0", CultureInfo.InvariantCulture));
        Stat("Extra paths", space.Sum(group => group.Files.Count - 1).ToString("N0", CultureInfo.InvariantCulture));
        Stat("Reclaimable", ByteSize.Format(space.Sum(group => group.ReclaimableBytes)), Tone.Accent);
        if (links.Count > 0)
            Stat("Hard-link groups", links.Count.ToString("N0", CultureInfo.InvariantCulture) + "  (no extra space)", Tone.Dim);
    }

    public static void PrintIssues(ScanResult result)
    {
        if (result.Issues.Count == 0)
            return;

        ConsoleUi.Line("");
        ConsoleUi.Line("Some paths could not be read.", Tone.Warn);
        foreach (var issue in result.Issues.Take(8))
            ConsoleUi.Line("  " + issue.Path, Tone.Dim);

        var hidden = result.ReadErrors - Math.Min(8, result.Issues.Count);
        if (hidden > 0)
            ConsoleUi.Line($"  {hidden.ToString("N0", CultureInfo.InvariantCulture)} more were not listed.", Tone.Dim);
    }

    public static void PrintPlan(ActionPlan plan, KeepPolicy keep, string verb, string? batch)
    {
        ConsoleUi.Line("");
        ConsoleUi.Line($"Keep rule: {Keeper.Label(keep)}");
        ConsoleUi.Line($"Extra paths: {plan.ExtraFiles.ToString("N0", CultureInfo.InvariantCulture)}");
        ConsoleUi.Line($"Planned reclaim: {ByteSize.Format(plan.ReclaimableBytes)}");
        if (batch is not null)
            ConsoleUi.Line("Destination: " + batch);

        ConsoleUi.Line("");
        var shown = 0;
        const int max = 24;
        foreach (var group in plan.Groups)
        {
            if (shown >= max)
                break;

            ConsoleUi.Line("  KEEP  " + group.Keeper.FullPath, Tone.Good);
            foreach (var extra in group.Extras)
            {
                if (shown >= max)
                    break;

                var note = string.Equals(extra.Identity, group.Keeper.Identity, StringComparison.Ordinal)
                    ? "  (hard link, no space freed)"
                    : "";
                ConsoleUi.Line("  " + verb.PadRight(6) + " " + extra.FullPath + note);
                shown++;
            }
        }

        var hidden = plan.ExtraFiles - shown;
        if (hidden > 0)
            ConsoleUi.Line($"  ... {hidden.ToString("N0", CultureInfo.InvariantCulture)} more");
    }

    public static void PrintOutcome(ActionOutcome outcome, bool moved, bool recycle, bool removeEmpty, string? batch)
    {
        ConsoleUi.Line("");
        if (outcome.Removed == 0 && outcome.Failed == 0)
        {
            ConsoleUi.Line("Nothing was changed.");
            return;
        }

        var files = Count(outcome.Removed, "file", "files");
        if (moved)
            ConsoleUi.Line($"Moved {files} to {batch}.", outcome.Failed == 0 ? Tone.Good : Tone.Warn);
        else if (recycle)
            ConsoleUi.Line($"Sent {files} to the Recycle Bin.", outcome.Failed == 0 ? Tone.Good : Tone.Warn);
        else
            ConsoleUi.Line($"Deleted {files}.", outcome.Failed == 0 ? Tone.Good : Tone.Warn);

        if (removeEmpty)
        {
            if (outcome.DirectoriesRemoved > 0)
            {
                ConsoleUi.Line($"Removed {Count(outcome.DirectoriesRemoved, "empty folder", "empty folders")}.");
                foreach (var directory in outcome.RemovedDirectories.Take(12))
                    ConsoleUi.Line("  " + directory, Tone.Dim);

                if (outcome.RemovedDirectories.Count > 12)
                    ConsoleUi.Line($"  {(outcome.RemovedDirectories.Count - 12).ToString("N0", CultureInfo.InvariantCulture)} more.", Tone.Dim);
            }
            else
            {
                ConsoleUi.Line("No empty folders were left.");
            }
        }

        if (outcome.Failed > 0)
        {
            ConsoleUi.Line($"{Count(outcome.Failed, "path", "paths")} failed.", Tone.Bad);
            foreach (var failure in outcome.Failures.Take(8))
                ConsoleUi.Line("  " + failure, Tone.Warn);

            if (outcome.Failures.Count > 8)
                ConsoleUi.Line($"  {(outcome.Failures.Count - 8).ToString("N0", CultureInfo.InvariantCulture)} more.", Tone.Dim);
        }
    }

    static void Stat(string label, string value, Tone tone = Tone.Normal) =>
        ConsoleUi.Line("  " + label.PadRight(26) + value, tone);

    static string Count(int count, string singular, string plural) =>
        count == 1
            ? "1 " + singular
            : count.ToString("N0", CultureInfo.InvariantCulture) + " " + plural;
}
