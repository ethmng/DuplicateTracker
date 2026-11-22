namespace DuplicateTracker;

internal static class Prompts
{
    public static KeepPolicy AskKeep(KeepPolicy current, CancellationToken cancellationToken)
    {
        ConsoleUi.Line("");
        ConsoleUi.Line("Which file should stay?");
        ConsoleUi.Line("  1  newest");
        ConsoleUi.Line("  2  oldest");
        ConsoleUi.Line("  3  shortest path");
        var answer = ConsoleUi.ReadLine($"Choice [{Keeper.Label(current)}]: ", cancellationToken);
        return (answer ?? "") switch
        {
            "1" or "newest" => KeepPolicy.Newest,
            "2" or "oldest" => KeepPolicy.Oldest,
            "3" or "shortest" or "shortest-path" => KeepPolicy.ShortestPath,
            _ => current
        };
    }

    public static DeleteStyle AskDeleteStyle(CancellationToken cancellationToken)
    {
        ConsoleUi.Line("");
        ConsoleUi.Line("Where should the extra files go?");
        ConsoleUi.Line("  1  Recycle Bin");
        ConsoleUi.Line("  2  Delete permanently");
        var answer = ConsoleUi.ReadLine("Choice [1]: ", cancellationToken);
        return answer is "2" or "permanent" ? DeleteStyle.Permanent : DeleteStyle.Recycle;
    }

    public static bool ConfirmPermanent(CancellationToken cancellationToken)
    {
        ConsoleUi.Line("Permanent delete cannot be undone from here.", Tone.Warn);
        var typed = ConsoleUi.ReadLine("Type DELETE to confirm: ", cancellationToken);
        return string.Equals(typed, "DELETE", StringComparison.Ordinal);
    }

    public static bool AskEmpty(IReadOnlyList<string> directories, CancellationToken cancellationToken)
    {
        if (directories.Count == 0)
        {
            ConsoleUi.Line("No folders will be left empty.");
            return false;
        }

        ConsoleUi.Line("");
        ConsoleUi.Line("These folders will be empty afterwards:");
        foreach (var directory in directories.Take(12))
            ConsoleUi.Line("  " + directory, Tone.Dim);

        if (directories.Count > 12)
            ConsoleUi.Line($"  {(directories.Count - 12).ToString("N0", CultureInfo.InvariantCulture)} more.", Tone.Dim);

        return ConsoleUi.AskYesNo("Remove those empty folders?", false, cancellationToken);
    }
}

internal static class Session
{
    public static int Interactive(CancellationToken cancellationToken)
    {
        ConsoleUi.PrintLogo();
        var settings = new ScanSettings();
        while (!cancellationToken.IsCancellationRequested)
        {
            var width = Math.Clamp(ConsoleUi.WindowWidth() - 2, 40, 72);
            ConsoleUi.Line(Ascii.Section("Menu", width), Tone.Dim);
            ConsoleUi.Line("  1  Scan a folder");
            ConsoleUi.Line("  2  Signature of one file");
            ConsoleUi.Line("  3  Settings");
            ConsoleUi.Line("  4  Help");
            ConsoleUi.Line("  5  Quit");
            ConsoleUi.Line("");
            ConsoleUi.Line("  " + SettingsText.Brief(settings), Tone.Dim);

            var answer = Norm(ConsoleUi.ReadCommand(cancellationToken));
            switch (answer)
            {
                case "1" or "scan":
                    ScanFolder(settings, cancellationToken);
                    break;
                case "2" or "hash":
                    HashPrompt(cancellationToken);
                    break;
                case "3" or "settings":
                    EditSettings(settings, cancellationToken);
                    break;
                case "4" or "help" or "?":
                    ConsoleUi.Line("");
                    ConsoleUi.Line(CommandLine.Help());
                    ConsoleUi.Line("");
                    break;
                case "5" or "q" or "quit" or "exit":
                    return ExitCodes.Ok;
                case "":
                    break;
                case null:
                    return ExitCodes.Ok;
                default:
                    ConsoleUi.Line("Use 1-5, or type help.", Tone.Warn);
                    break;
            }
        }

        return ExitCodes.Ok;
    }

    static void ScanFolder(ScanSettings settings, CancellationToken cancellationToken)
    {
        ConsoleUi.Line("");
        ConsoleUi.Line("  " + SettingsText.Brief(settings), Tone.Dim);
        var first = ConsoleUi.ReadLine("Folder to scan: ", cancellationToken);
        if (string.IsNullOrWhiteSpace(first))
            return;

        if (!Directory.Exists(first))
        {
            ConsoleUi.Error($"Folder not found: {first}");
            return;
        }

        var paths = new List<string> { first };
        while (ConsoleUi.AskYesNo("Add another folder?", false, cancellationToken))
        {
            var next = ConsoleUi.ReadLine("Folder: ", cancellationToken);
            if (string.IsNullOrWhiteSpace(next))
                break;

            if (!Directory.Exists(next))
            {
                ConsoleUi.Error($"Folder not found: {next}");
                continue;
            }

            paths.Add(next);
        }

        var command = new AppCommand
        {
            Kind = AppCommandKind.Scan,
            Settings = settings,
            ReviewAfter = true
        };
        command.Paths.AddRange(paths);
        App.RunScan(command, cancellationToken);
    }

    static void HashPrompt(CancellationToken cancellationToken)
    {
        var path = ConsoleUi.ReadLine("File: ", cancellationToken);
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            App.PrintHash(path, cancellationToken);
        }
        catch (CommandException ex)
        {
            ConsoleUi.Error(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ConsoleUi.Error(ex.Message);
        }
    }

    static void EditSettings(ScanSettings settings, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ConsoleUi.Line("");
            ConsoleUi.Line("  1  Minimum size          " + ByteSize.Format(settings.MinSize));
            ConsoleUi.Line("  2  Extensions            " + ListOr(settings.Extensions, "all"));
            ConsoleUi.Line("  3  Skip extensions       " + ListOr(settings.ExcludeExtensions, "none"));
            ConsoleUi.Line("  4  Skip folder names     " + ListOr(settings.ExcludeDirectories, "none"));
            ConsoleUi.Line("  5  Subfolders            " + Yes(settings.Recurse));
            ConsoleUi.Line("  6  Empty files           " + (settings.IncludeEmpty ? "include" : "skip"));
            ConsoleUi.Line("  7  Cloud-only files      " + (settings.IncludeCloud ? "include" : "skip"));
            ConsoleUi.Line("  8  Directory links       " + (settings.FollowLinks ? "follow, inside the scan" : "skip"));
            ConsoleUi.Line("  9  Threads               " + settings.Threads.ToString(CultureInfo.InvariantCulture));
            ConsoleUi.Line("  0  Cache files           " + (settings.SkipCacheFiles ? "skip thumbs.db and desktop.ini" : "include"));
            ConsoleUi.Line("  b  Back");

            var answer = Norm(ConsoleUi.ReadCommand(cancellationToken));
            switch (answer)
            {
                case "1":
                    var sizeText = ConsoleUi.ReadLine("Minimum size (10KB, 1MB, or bytes): ", cancellationToken);
                    if (!string.IsNullOrWhiteSpace(sizeText) && ByteSize.TryParse(sizeText, out var size))
                        settings.MinSize = size;
                    else if (!string.IsNullOrWhiteSpace(sizeText))
                        ConsoleUi.Line("Could not read that size.", Tone.Warn);
                    break;
                case "2":
                    ReplaceList(settings.Extensions, "Extensions to keep (blank = all): ", trimDot: true, cancellationToken);
                    break;
                case "3":
                    ReplaceList(settings.ExcludeExtensions, "Extensions to skip (blank = none): ", trimDot: true, cancellationToken);
                    break;
                case "4":
                    ReplaceList(settings.ExcludeDirectories, "Folder names to skip (blank = none): ", trimDot: false, cancellationToken);
                    break;
                case "5":
                    settings.Recurse = !settings.Recurse;
                    break;
                case "6":
                    settings.IncludeEmpty = !settings.IncludeEmpty;
                    break;
                case "7":
                    settings.IncludeCloud = !settings.IncludeCloud;
                    break;
                case "8":
                    settings.FollowLinks = !settings.FollowLinks;
                    break;
                case "9":
                    var threadsText = ConsoleUi.ReadLine("Threads (1-32): ", cancellationToken);
                    if (int.TryParse(threadsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var threads) && threads is >= 1 and <= 32)
                        settings.Threads = threads;
                    else if (!string.IsNullOrWhiteSpace(threadsText))
                        ConsoleUi.Line("Use a number from 1 to 32.", Tone.Warn);
                    break;
                case "0":
                    settings.SkipCacheFiles = !settings.SkipCacheFiles;
                    break;
                case "b" or "back" or "q" or "":
                    return;
                case null:
                    return;
                default:
                    ConsoleUi.Line("Use 0-9, or b to go back.", Tone.Warn);
                    break;
            }
        }
    }

    static void ReplaceList(HashSet<string> target, string prompt, bool trimDot, CancellationToken cancellationToken)
    {
        var raw = ConsoleUi.ReadLine(prompt, cancellationToken);
        target.Clear();
        if (string.IsNullOrWhiteSpace(raw))
            return;

        foreach (var part in raw.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var item = trimDot ? part.TrimStart('.') : part;
            if (item.Length > 0)
                target.Add(item);
        }
    }

    static string ListOr(HashSet<string> values, string fallback) =>
        values.Count == 0 ? fallback : string.Join(", ", values.OrderBy(item => item, StringComparer.OrdinalIgnoreCase));

    static string Yes(bool value) => value ? "yes" : "no";

    static string? Norm(string? value) => value?.Trim().ToLowerInvariant();
}

internal static class Review
{
    const int PageSize = 8;

    public static int Run(ScanResult result, AppCommand command, CancellationToken cancellationToken)
    {
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        var page = 0;
        while (true)
        {
            if (result.Groups.Count == 0)
            {
                ConsoleUi.Line("");
                ConsoleUi.Line("No duplicate groups left in this scan.");
                ConsoleUi.Line("  e export    q leave", Tone.Dim);
            }
            else
            {
                PrintPage(result, page);
            }

            var raw = ConsoleUi.ReadCommand(cancellationToken);
            if (raw is null)
                return ExitCodes.For(result, null, command.FailOnDuplicates);

            var (cmd, arg) = Split(raw);
            switch (cmd)
            {
                case "q" or "quit" or "exit":
                    return ExitCodes.For(result, null, command.FailOnDuplicates);
                case "n" or "next":
                    page = Math.Min(page + 1, LastPage(result.Groups.Count));
                    break;
                case "p" or "prev" or "previous":
                    page = Math.Max(0, page - 1);
                    break;
                case "?" or "help":
                    PrintHelp();
                    break;
                case "e" or "export":
                    Export(result, command, overrides, arg, cancellationToken);
                    break;
                case "k" or "keep":
                    command.Keep = Prompts.AskKeep(command.Keep, cancellationToken);
                    command.KeepExplicit = true;
                    break;
                case "d" or "delete":
                    Commit(result, command, overrides, hardLinksOnly: false, move: false, arg, cancellationToken);
                    page = Math.Min(page, LastPage(result.Groups.Count));
                    break;
                case "l" or "links":
                    Commit(result, command, overrides, hardLinksOnly: true, move: false, arg, cancellationToken);
                    page = Math.Min(page, LastPage(result.Groups.Count));
                    break;
                case "m" or "move":
                    Commit(result, command, overrides, hardLinksOnly: false, move: true, arg, cancellationToken);
                    page = Math.Min(page, LastPage(result.Groups.Count));
                    break;
                case "g" or "open" or "group":
                    if (TryNumber(arg, result.Groups.Count, cancellationToken, out var number))
                        Open(result, result.Groups[number - 1], command, overrides, cancellationToken);
                    break;
                case "":
                    break;
                default:
                    if (int.TryParse(cmd, NumberStyles.Integer, CultureInfo.InvariantCulture, out var typed)
                        && typed >= 1
                        && typed <= result.Groups.Count)
                    {
                        Open(result, result.Groups[typed - 1], command, overrides, cancellationToken);
                    }
                    else
                    {
                        ConsoleUi.Line("Unknown command. Type ? for the list.", Tone.Warn);
                    }

                    break;
            }
        }
    }

    static void PrintPage(ScanResult result, int page)
    {
        var width = Math.Clamp(ConsoleUi.WindowWidth() - 2, 48, 110);
        var from = page * PageSize;
        var slice = result.Groups.Skip(from).Take(PageSize).ToList();
        var last = Math.Max(from + slice.Count, from + 1);
        ConsoleUi.Line("");
        ConsoleUi.Line(Ascii.Section($"Groups  {from + 1}-{last} of {result.Groups.Count}", width), Tone.Dim);
        if (result.HardLinkGroups.Any())
            ConsoleUi.Line("  \"names\" means hard links. Those rows do not take extra space.", Tone.Dim);

        ConsoleUi.Line($"{"#",5}  {"Paths",5}  {"Reclaim",10}  {"Signature",-12}  Sample", Tone.Dim);
        for (var index = 0; index < slice.Count; index++)
        {
            var group = slice[index];
            var number = from + index + 1;
            var reclaim = group.IsHardLinkOnly ? "names" : ByteSize.Format(group.ReclaimableBytes);
            var sampleWidth = Math.Max(16, width - 46);
            var sample = Ascii.TrimPath(group.Files[0].FullPath, sampleWidth);
            var signature = group.Signature.Length >= 12 ? group.Signature[..12] : group.Signature;
            ConsoleUi.Line($"{number,5}  {group.Files.Count,5}  {reclaim,10}  {signature,-12}  {sample}");
        }

        ConsoleUi.Line("");
        ConsoleUi.Line("  g <n> open    n next    p prev    d delete    m move", Tone.Dim);
        ConsoleUi.Line("  l link names  k keep    e export  q leave     ? help", Tone.Dim);
    }

    static void PrintHelp()
    {
        ConsoleUi.Line("");
        ConsoleUi.Line("  g 4          Open group 4");
        ConsoleUi.Line("  d            Delete extra copies that take space");
        ConsoleUi.Line("  m D:\\Keep    Move those copies under D:\\Keep");
        ConsoleUi.Line("  l            Remove extra hard-link names");
        ConsoleUi.Line("  k            Change which file is kept");
        ConsoleUi.Line("  e report.csv Save a report of the groups still listed");
        ConsoleUi.Line("  n / p        Next or previous page");
        ConsoleUi.Line("  q            Leave this scan");
        ConsoleUi.Line("");
        ConsoleUi.Line("Inside a group: keep 2, open 2, show 2, d, m, b");
    }

    static void Open(
        ScanResult result,
        DuplicateGroup group,
        AppCommand command,
        Dictionary<string, string> overrides,
        CancellationToken cancellationToken)
    {
        while (result.Groups.Contains(group))
        {
            PrintGroup(group, command.Keep, overrides);
            var raw = ConsoleUi.ReadCommand(cancellationToken);
            if (raw is null)
                return;

            var (cmd, arg) = Split(raw);
            switch (cmd)
            {
                case "b" or "back" or "q" or "quit":
                    return;
                case "keep":
                    if (TryFile(arg, group, cancellationToken, out var keepIndex))
                    {
                        overrides[group.Signature] = group.Files[keepIndex].FullPath;
                        command.KeepExplicit = true;
                    }

                    break;
                case "open" or "o":
                    if (TryFile(arg, group, cancellationToken, out var openIndex))
                        ShellOpen(group.Files[openIndex].FullPath, reveal: false);
                    break;
                case "show" or "f" or "reveal":
                    if (TryFile(arg, group, cancellationToken, out var showIndex))
                        ShellOpen(group.Files[showIndex].FullPath, reveal: true);
                    break;
                case "d" or "delete":
                    Commit(result, command, overrides, hardLinksOnly: group.IsHardLinkOnly, move: false, arg: null, cancellationToken, group);
                    break;
                case "m" or "move":
                    Commit(result, command, overrides, hardLinksOnly: group.IsHardLinkOnly, move: true, arg, cancellationToken, group);
                    break;
                case "?" or "help":
                    ConsoleUi.Line("  keep <n>   open <n>   show <n>   d delete others   m move others   b back");
                    break;
                default:
                    if (cmd.Length > 0)
                        ConsoleUi.Line("Unknown command. Type ? for the list.", Tone.Warn);
                    break;
            }
        }
    }

    static void PrintGroup(DuplicateGroup group, KeepPolicy keep, IReadOnlyDictionary<string, string> overrides)
    {
        var keeper = Keeper.Resolve(group, keep, overrides);
        ConsoleUi.Line("");
        ConsoleUi.Line("  Signature    " + group.Signature);
        ConsoleUi.Line("  Size         " + ByteSize.Format(group.Length));
        if (group.IsHardLinkOnly)
            ConsoleUi.Line("  These names point at one copy of the data.");
        else
            ConsoleUi.Line("  Reclaimable  " + ByteSize.Format(group.ReclaimableBytes) + " if one copy is kept", Tone.Accent);

        for (var index = 0; index < group.Files.Count; index++)
        {
            var file = group.Files[index];
            var mark = PathRules.Same(file.FullPath, keeper.FullPath) ? "KEEP" : "    ";
            var when = file.LastWriteTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            var links = file.LinkCount > 1 ? "   links " + file.LinkCount.ToString(CultureInfo.InvariantCulture) : "";
            ConsoleUi.Line($"  {index + 1,2}  {mark}  {file.FullPath}", PathRules.Same(file.FullPath, keeper.FullPath) ? Tone.Good : Tone.Normal);
            ConsoleUi.Line($"        modified {when}{links}", Tone.Dim);
        }

        ConsoleUi.Line("");
        ConsoleUi.Line("  keep <n>   open <n>   show <n>   d delete others   m move others   b back", Tone.Dim);
    }

    static void Commit(
        ScanResult result,
        AppCommand command,
        Dictionary<string, string> overrides,
        bool hardLinksOnly,
        bool move,
        string? arg,
        CancellationToken cancellationToken,
        DuplicateGroup? only = null)
    {
        if (!command.KeepExplicit)
        {
            command.Keep = Prompts.AskKeep(command.Keep, cancellationToken);
            command.KeepExplicit = true;
        }

        var plan = only is not null
            ? Planner.Create([only], command.Keep, overrides, includeHardLinkOnly: true)
            : hardLinksOnly
                ? Planner.Create(result.HardLinkGroups, command.Keep, overrides, includeHardLinkOnly: true)
                : Planner.Create(result.SpaceGroups, command.Keep, overrides, includeHardLinkOnly: false);

        if (plan.ExtraFiles == 0)
        {
            ConsoleUi.Line(hardLinksOnly
                ? "No extra hard-link names."
                : "No extra copies that take space.");
            var leftOut = result.HardLinkGroups.Count();
            if (!hardLinksOnly && only is null && leftOut > 0)
                ConsoleUi.Line($"{leftOut.ToString("N0", CultureInfo.InvariantCulture)} hard-link groups were left out. Use l to remove extra names.", Tone.Dim);
            return;
        }

        string? batch = null;
        if (move)
        {
            var destination = arg;
            if (string.IsNullOrWhiteSpace(destination))
                destination = ConsoleUi.ReadLine("Move extras to folder: ", cancellationToken);

            if (string.IsNullOrWhiteSpace(destination))
            {
                ConsoleUi.Line("Nothing was changed.");
                return;
            }

            var full = Path.GetFullPath(destination);
            if (result.Roots.Any(root => PathRules.IsSameOrChild(full, root)))
            {
                ConsoleUi.Error("That folder is inside the scan. Pick a folder outside it.");
                return;
            }

            batch = FileActions.BatchFolder(full);
        }

        if (!hardLinksOnly && only is null)
        {
            var skipped = result.HardLinkGroups.Count();
            if (skipped > 0)
                ConsoleUi.Line($"{skipped.ToString("N0", CultureInfo.InvariantCulture)} hard-link groups are not in this plan.", Tone.Dim);
        }

        Presentation.PrintPlan(plan, command.Keep, move ? "move" : "delete", batch);
        var question = hardLinksOnly
            ? "Remove the extra names?"
            : move ? "Move these files?" : "Remove these extra files?";
        if (!ConsoleUi.AskYesNo(question, false, cancellationToken))
        {
            ConsoleUi.Line("Nothing was changed.");
            return;
        }

        var recycle = true;
        if (!move)
        {
            var style = Prompts.AskDeleteStyle(cancellationToken);
            if (style == DeleteStyle.Permanent && !Prompts.ConfirmPermanent(cancellationToken))
            {
                ConsoleUi.Line("Nothing was changed.");
                return;
            }

            recycle = style != DeleteStyle.Permanent;
        }

        var paths = plan.Groups.SelectMany(group => group.Extras.Select(file => file.FullPath)).ToList();
        var predicted = FileActions.PredictEmptyDirectories(result.Roots, paths);
        var removeEmpty = Prompts.AskEmpty(predicted, cancellationToken);
        var outcome = move
            ? FileActions.Move(plan, result.Roots, batch!, removeEmpty)
            : FileActions.Delete(plan, result.Roots, recycle, removeEmpty);

        Presentation.PrintOutcome(outcome, move, recycle, removeEmpty, batch);
        FileActions.ForgetRemoved(result.Groups, outcome.RemovedPaths);
    }

    static void Export(
        ScanResult result,
        AppCommand command,
        IReadOnlyDictionary<string, string> overrides,
        string? arg,
        CancellationToken cancellationToken)
    {
        var path = arg;
        if (string.IsNullOrWhiteSpace(path))
            path = ConsoleUi.ReadLine("Report path (.csv, .json, or .txt) [duplicates.csv]: ", cancellationToken);

        if (string.IsNullOrWhiteSpace(path))
            path = "duplicates.csv";

        try
        {
            ReportWriter.EnsureExtension(path);
            if (File.Exists(path) && !ConsoleUi.AskYesNo("Replace " + path + "?", false, cancellationToken))
                return;

            ReportWriter.Write(path, result, command.Keep, overrides);
            ConsoleUi.Line("Report written to " + Path.GetFullPath(path), Tone.Good);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CommandException)
        {
            ConsoleUi.Error(ex.Message);
        }
    }

    static void ShellOpen(string path, bool reveal)
    {
        try
        {
            if (reveal)
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
                return;
            }

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            ConsoleUi.Error(ex.Message);
        }
    }

    static bool TryNumber(string? arg, int count, CancellationToken cancellationToken, out int number)
    {
        number = 0;
        if (count == 0)
        {
            ConsoleUi.Line("There are no groups.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(arg))
            arg = ConsoleUi.ReadLine("Group number: ", cancellationToken);

        if (!int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) || number < 1 || number > count)
        {
            ConsoleUi.Line($"Use a group number from 1 to {count.ToString(CultureInfo.InvariantCulture)}.", Tone.Warn);
            return false;
        }

        return true;
    }

    static bool TryFile(string? arg, DuplicateGroup group, CancellationToken cancellationToken, out int index)
    {
        index = 0;
        if (string.IsNullOrWhiteSpace(arg))
            arg = ConsoleUi.ReadLine("File number: ", cancellationToken);

        if (!int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number < 1 || number > group.Files.Count)
        {
            ConsoleUi.Line($"Use a file number from 1 to {group.Files.Count.ToString(CultureInfo.InvariantCulture)}.", Tone.Warn);
            return false;
        }

        index = number - 1;
        return true;
    }

    static int LastPage(int count) => count == 0 ? 0 : (count - 1) / PageSize;

    static (string Cmd, string? Arg) Split(string raw)
    {
        var parts = raw.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return ("", null);

        var cmd = parts[0].ToLowerInvariant();
        var arg = parts.Length > 1 ? parts[1].Trim().Trim('"') : null;
        return (cmd, string.IsNullOrEmpty(arg) ? null : arg);
    }
}
