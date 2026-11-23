namespace DuplicateTracker;

internal static class CommandLine
{
    public static AppCommand Parse(string[] args)
    {
        if (args.Length == 0)
            return new AppCommand { Kind = AppCommandKind.Interactive, ReviewAfter = true };

        var head = args[0].ToLowerInvariant();
        if (head is "--version" or "-v" or "version")
            return new AppCommand { Kind = AppCommandKind.Version };

        if (head is "--help" or "-h" or "/?" or "help")
            return new AppCommand { Kind = AppCommandKind.Help };

        if (head is "hash")
        {
            if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
                throw new CommandException("Usage: DuplicateTracker hash <file>");

            if (args.Length > 2)
                throw new CommandException("hash takes one file.");

            return new AppCommand { Kind = AppCommandKind.Hash, HashPath = args[1] };
        }

        var index = 0;
        if (head is "scan")
            index = 1;
        else if (!(head.StartsWith('-') || LooksLikePath(args[0])))
            throw new CommandException($"Unknown command '{args[0]}'. Run DuplicateTracker --help.");

        var command = new AppCommand { Kind = AppCommandKind.Scan };
        for (; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg is "--help" or "-h" or "/?")
                return new AppCommand { Kind = AppCommandKind.Help };

            if (arg is "--version" or "-v")
                return new AppCommand { Kind = AppCommandKind.Version };

            if (!arg.StartsWith('-'))
            {
                command.Paths.Add(arg);
                continue;
            }

            var (name, inline) = Split(arg);
            switch (name)
            {
                case "--min-size":
                    var sizeText = Value(args, ref index, inline, name);
                    if (!ByteSize.TryParse(sizeText, out var size))
                        throw new CommandException($"Could not read the size '{sizeText}'. Try 10KB, 1MB, 1.5GB, or a number of bytes.");
                    command.Settings.MinSize = size;
                    break;
                case "--ext":
                    AddList(command.Settings.Extensions, Value(args, ref index, inline, name), trimDot: true);
                    break;
                case "--exclude-ext":
                    AddList(command.Settings.ExcludeExtensions, Value(args, ref index, inline, name), trimDot: true);
                    break;
                case "--exclude-dir":
                    AddList(command.Settings.ExcludeDirectories, Value(args, ref index, inline, name), trimDot: false);
                    break;
                case "--threads":
                    var threadsText = Value(args, ref index, inline, name);
                    if (!int.TryParse(threadsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var threads) || threads < 1 || threads > 32)
                        throw new CommandException("--threads needs a number from 1 to 32.");
                    command.Settings.Threads = threads;
                    break;
                case "--keep":
                    command.Keep = ParseKeep(Value(args, ref index, inline, name));
                    command.KeepExplicit = true;
                    break;
                case "--move":
                    command.MoveTo = Value(args, ref index, inline, name);
                    break;
                case "--export":
                    command.ExportPath = Value(args, ref index, inline, name);
                    ReportWriter.EnsureExtension(command.ExportPath);
                    break;
                case "--include-empty":
                    command.Settings.IncludeEmpty = true;
                    break;
                case "--include-cloud":
                    command.Settings.IncludeCloud = true;
                    break;
                case "--include-cache-files":
                    command.Settings.SkipCacheFiles = false;
                    break;
                case "--follow-links":
                    command.Settings.FollowLinks = true;
                    break;
                case "--no-recurse":
                    command.Settings.Recurse = false;
                    break;
                case "--delete":
                    command.Delete = true;
                    break;
                case "--permanent":
                    if (command.DeleteStyle == DeleteStyle.Recycle)
                        throw new CommandException("--recycle and --permanent cannot be used together.");
                    command.Delete = true;
                    command.DeleteStyle = DeleteStyle.Permanent;
                    break;
                case "--recycle":
                    if (command.DeleteStyle == DeleteStyle.Permanent)
                        throw new CommandException("--recycle and --permanent cannot be used together.");
                    command.Delete = true;
                    command.DeleteStyle = DeleteStyle.Recycle;
                    break;
                case "--remove-empty-dirs":
                    command.RemoveEmptyDirs = true;
                    break;
                case "--yes":
                    command.Yes = true;
                    break;
                case "--dry-run":
                    command.DryRun = true;
                    break;
                case "--fail-on-duplicates":
                    command.FailOnDuplicates = true;
                    break;
                case "--quiet":
                    command.Quiet = true;
                    break;
                default:
                    throw new CommandException($"Unknown option '{name}'.");
            }
        }

        if (command.Paths.Count == 0)
            throw new CommandException("Tell DuplicateTracker which folder to scan.");

        if (command.Delete && command.MoveTo is not null)
            throw new CommandException("--delete and --move cannot be used together.");

        if (command.RemoveEmptyDirs && !command.Delete && command.MoveTo is null)
            throw new CommandException("Use --remove-empty-dirs with --delete or --move.");

        if (command.DeleteStyle == DeleteStyle.Permanent && command.MoveTo is not null)
            throw new CommandException("--permanent applies to --delete, not --move.");

        foreach (var path in command.Paths)
        {
            if (!Directory.Exists(path))
                throw new CommandException($"Folder not found: {path}");
        }

        if (command.MoveTo is not null)
        {
            var destination = Path.GetFullPath(command.MoveTo);
            if (command.Paths.Any(root => PathRules.IsSameOrChild(destination, root)))
                throw new CommandException("The destination is inside a folder you are scanning. Pick a folder outside it.");
        }

        command.ReviewAfter = !command.Quiet
            && !command.Delete
            && command.MoveTo is null
            && !command.DryRun;

        return command;
    }

    public static string Help() =>
        $"""
        {AppInfo.Name} {AppInfo.Version}
        Find files that are byte-for-byte the same, then review, move, or delete the extra copies.

        Usage
          DuplicateTracker
          DuplicateTracker scan <folder> [more folders] [options]
          DuplicateTracker hash <file>
          DuplicateTracker --help
          DuplicateTracker --version

        At the menu, press Enter when a folder or a file is asked. Windows opens
        its dialog. Type a path if you already have one. A redirected script does
        not open the dialog.

        Scan options
          --min-size <size>         Skip smaller files. Examples: 512, 10KB, 1MB, 1.5GB
          --include-empty           Treat empty files as one duplicate group
          --ext <list>              Only these extensions. Example: jpg,png,pdf
          --exclude-ext <list>      Skip these extensions
          --exclude-dir <list>      Skip folders with these names. Example: .git,node_modules
          --include-cloud           Hash files that are not stored on this PC
          --include-cache-files     Include thumbs.db, desktop.ini, and .DS_Store
          --follow-links            Follow directory links that stay inside the scan
          --no-recurse              Do not enter subfolders
          --threads <n>             Hashing threads, from 1 to 32. Default is up to 4

        After the scan
          --export <file>           Write .csv, .json, or .txt. Written before any delete or move
          --delete                  Remove extra copies. One file in each group stays
          --recycle                 Send those copies to the Recycle Bin. This is the default
          --permanent               Delete those copies without using the Recycle Bin
          --move <folder>           Move extra copies into a dated folder under this path
          --keep <rule>             newest, oldest, or shortest-path. Default is newest
          --remove-empty-dirs       Remove folders left empty inside the scanned folders
          --yes                     Apply --delete or --move. Without it, a console asks first
          --dry-run                 Print the plan and change nothing
          --fail-on-duplicates      Exit with code 1 when any duplicate group is found
          --quiet                   Skip the banner and the progress line

        Exit codes
          0    Done
          1    Duplicates found, and --fail-on-duplicates was set
          2    The command line could not be read
          3    Some files could not be read
          4    A delete or a move did not finish for every file
          130  Stopped with Ctrl+C

        Two files match when their SHA-256 signatures match. Files with a unique size are not
        opened. Hard-link groups count for --fail-on-duplicates, and they are left out of a
        bulk delete because removing a name does not free space.

        Examples
          DuplicateTracker scan D:\Photos
          DuplicateTracker scan D:\Photos E:\Backup --min-size 1MB --export dupes.csv
          DuplicateTracker scan D:\Photos --delete --keep newest --remove-empty-dirs --yes
          DuplicateTracker scan D:\Photos --move D:\Quarantine --keep shortest-path --yes
        """;

    static bool LooksLikePath(string value) =>
        value.Contains('\\') || value.Contains('/') || (value.Length >= 2 && value[1] == ':') || Directory.Exists(value);

    static (string Name, string? Inline) Split(string arg)
    {
        var equals = arg.IndexOf('=');
        if (equals < 0)
            return (arg.ToLowerInvariant(), null);

        return (arg[..equals].ToLowerInvariant(), arg[(equals + 1)..]);
    }

    static string Value(string[] args, ref int index, string? inline, string name)
    {
        if (inline is not null)
            return inline;

        if (index + 1 >= args.Length || args[index + 1].StartsWith('-'))
            throw new CommandException($"{name} needs a value.");

        index++;
        return args[index];
    }

    static void AddList(HashSet<string> target, string raw, bool trimDot)
    {
        foreach (var part in raw.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var item = part;
            if (trimDot)
                item = item.TrimStart('.');

            if (item.Length > 0)
                target.Add(item);
        }
    }

    static KeepPolicy ParseKeep(string value) => value.Trim().ToLowerInvariant() switch
    {
        "newest" or "new" => KeepPolicy.Newest,
        "oldest" or "old" => KeepPolicy.Oldest,
        "shortest" or "shortest-path" or "shortestpath" or "path" => KeepPolicy.ShortestPath,
        _ => throw new CommandException("Keep rule must be newest, oldest, or shortest-path.")
    };
}
