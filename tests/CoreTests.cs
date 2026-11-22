using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace DuplicateTracker.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DuplicateTrackerTests", Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string File(params string[] parts) => System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

public class ByteSizeTests
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData("1", 1)]
    [InlineData("1024", 1024)]
    [InlineData("1KB", 1024)]
    [InlineData("1.5MB", 1_572_864)]
    [InlineData("2GB", 2L * 1024 * 1024 * 1024)]
    [InlineData("10 kb", 10 * 1024)]
    public void Parses_sizes(string text, long expected)
    {
        Assert.True(ByteSize.TryParse(text, out var bytes));
        Assert.Equal(expected, bytes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nope")]
    [InlineData("-1")]
    [InlineData("1XB")]
    public void Rejects_bad_sizes(string text) => Assert.False(ByteSize.TryParse(text, out _));

    [Fact]
    public void Formats_like_explorer()
    {
        Assert.Equal("0 B", ByteSize.Format(0));
        Assert.Equal("1 KB", ByteSize.Format(1024));
        Assert.Equal("1.5 KB", ByteSize.Format(1536));
    }
}

public class PathRulesTests
{
    [Fact]
    public void Child_checks_do_not_confuse_prefixes_or_drive_roots()
    {
        Assert.True(PathRules.IsStrictChild(@"C:\Photos\2020", @"C:\Photos"));
        Assert.False(PathRules.IsStrictChild(@"C:\PhotosBackup", @"C:\Photos"));
        Assert.False(PathRules.IsStrictChild(@"C:\", @"C:\"));
        Assert.True(PathRules.IsStrictChild(@"C:\Windows", @"C:\"));
        Assert.True(PathRules.Same(@"C:\Photos\", @"C:\Photos"));
    }
}

public class AsciiTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(60)]
    public void Logo_is_a_rectangle(int width)
    {
        var lines = Ascii.LogoFor(width, "1.0.0").ToArray();
        Assert.True(lines.Length >= 4);
        Assert.All(lines, line => Assert.Equal(lines[0].Length, line.Length));
        var picture = string.Join('\n', lines);
        Assert.Contains("DuplicateTracker", picture);
        Assert.Contains("SHA-256", picture);
        if (width >= 72)
            Assert.Contains("######  ######", picture);
    }

    [Fact]
    public void Monogram_columns_line_up()
    {
        Assert.Equal(14, "######  ######".Length);
        Assert.Equal(14, "##  ##    ##  ".Length);
        Assert.Equal(14, "######    ##  ".Length);
        Assert.Equal('#', "######  ######"[10]);
        Assert.Equal('#', "##  ##    ##  "[10]);
        Assert.Equal('#', "######    ##  "[10]);
    }
}

public class CommandLineTests
{
    [Fact]
    public void Parses_a_scan()
    {
        using var dir = new TempDir();
        var command = CommandLine.Parse(["scan", dir.Path, "--min-size=2MB", "--keep", "oldest", "--ext", ".jpg,png", "--delete", "--yes"]);
        Assert.Equal(AppCommandKind.Scan, command.Kind);
        Assert.Equal(2L * 1024 * 1024, command.Settings.MinSize);
        Assert.Equal(KeepPolicy.Oldest, command.Keep);
        Assert.Contains("jpg", command.Settings.Extensions);
        Assert.Contains("png", command.Settings.Extensions);
        Assert.True(command.Delete);
        Assert.True(command.Yes);
        Assert.False(command.ReviewAfter);
    }

    [Fact]
    public void Rejects_delete_and_move_together()
    {
        using var dir = new TempDir();
        var error = Assert.Throws<CommandException>(() => CommandLine.Parse(["scan", dir.Path, "--delete", "--move", dir.Path + "out"]));
        Assert.Contains("--delete", error.Message);
    }

    [Fact]
    public void Rejects_a_missing_folder_and_a_bad_report()
    {
        Assert.Throws<CommandException>(() => CommandLine.Parse(["scan", @"C:\this\folder\does\not\exist"]));
        using var dir = new TempDir();
        Assert.Throws<CommandException>(() => CommandLine.Parse(["scan", dir.Path, "--export", "report.xml"]));
        Assert.Throws<CommandException>(() => CommandLine.Parse(["scan"]));
    }

    [Fact]
    public void Help_and_version_are_commands()
    {
        Assert.Equal(AppCommandKind.Help, CommandLine.Parse(["--help"]).Kind);
        Assert.Equal(AppCommandKind.Version, CommandLine.Parse(["--version"]).Kind);
        Assert.Equal(AppCommandKind.Interactive, CommandLine.Parse([]).Kind);
    }
}

public class ExitCodeTests
{
    [Fact]
    public void Action_failures_outrank_duplicate_detection()
    {
        var result = new ScanResult
        {
            Roots = [],
            Groups = [],
            Issues = [],
            DuplicatesFound = true,
            ReadErrors = 2
        };
        var action = new ActionOutcome { Failed = 1 };
        Assert.Equal(ExitCodes.ActionFailed, ExitCodes.For(result, action, failOnDuplicates: true));
        Assert.Equal(ExitCodes.Duplicates, ExitCodes.For(result, action: null, failOnDuplicates: true));
        result = new ScanResult { Roots = [], Groups = [], Issues = [], DuplicatesFound = false, ReadErrors = 1 };
        Assert.Equal(ExitCodes.ReadErrors, ExitCodes.For(result, action: null, failOnDuplicates: true));
    }
}

public class KeeperTests
{
    [Fact]
    public void Keep_rules_break_ties_on_path_length()
    {
        var older = File(@"D:\long\path\old.txt", new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), "1");
        var newer = File(@"D:\new.txt", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), "2");
        var tied = File(@"D:\also-new-but-longer.txt", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), "3");
        var group = new DuplicateGroup
        {
            Signature = "abc",
            Length = 10,
            Files = [older, newer, tied]
        };

        Assert.Equal(newer.FullPath, Keeper.Choose(group, KeepPolicy.Newest).FullPath);
        Assert.Equal(older.FullPath, Keeper.Choose(group, KeepPolicy.Oldest).FullPath);
        Assert.Equal(newer.FullPath, Keeper.Choose(group, KeepPolicy.ShortestPath).FullPath);

        var plan = Planner.Create([group], KeepPolicy.Newest, overrides: null, includeHardLinkOnly: false);
        Assert.Equal(newer.FullPath, Assert.Single(plan.Groups).Keeper.FullPath);
        Assert.DoesNotContain(plan.Groups[0].Extras, file => file.FullPath == newer.FullPath);
        Assert.Equal(2, plan.ExtraFiles);
    }

    static DuplicateFile File(string path, DateTime written, string identity) => new()
    {
        FullPath = path,
        Length = 10,
        LastWriteTimeUtc = written,
        Identity = identity,
        LinkCount = 1
    };
}

public class ScannerTests
{
    [Fact]
    public async Task Groups_files_with_the_same_bytes()
    {
        using var dir = new TempDir();
        await System.IO.File.WriteAllTextAsync(dir.File("a.txt"), "same-content");
        await System.IO.File.WriteAllTextAsync(dir.File("b.txt"), "same-content");
        Directory.CreateDirectory(dir.File("sub"));
        await System.IO.File.WriteAllTextAsync(dir.File("sub", "c.txt"), "same-content");
        await System.IO.File.WriteAllTextAsync(dir.File("unique.txt"), "different");

        var result = await Scan(dir.Path);
        var group = Assert.Single(result.Groups);
        Assert.Equal(3, group.Files.Count);
        Assert.Equal(group.Length * 2, group.ReclaimableBytes);
        Assert.False(group.IsHardLinkOnly);
        Assert.Equal(64, group.Signature.Length);
    }

    [Fact]
    public async Task Same_prefix_is_not_enough_when_the_rest_differs()
    {
        using var dir = new TempDir();
        var shared = new string('a', DuplicateFinder.PrefixLength);
        await System.IO.File.WriteAllTextAsync(dir.File("one.bin"), shared + new string('b', 4096));
        await System.IO.File.WriteAllTextAsync(dir.File("two.bin"), shared + new string('c', 4096));
        await System.IO.File.WriteAllTextAsync(dir.File("three.bin"), shared + new string('b', 4096));

        var result = await Scan(dir.Path);
        var group = Assert.Single(result.Groups);
        Assert.Equal(2, group.Files.Count);
        Assert.Contains(group.Files, file => file.FullPath.EndsWith("one.bin", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(group.Files, file => file.FullPath.EndsWith("three.bin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Filters_size_extension_directory_empty_and_cache_files()
    {
        using var dir = new TempDir();
        await System.IO.File.WriteAllTextAsync(dir.File("a.jpg"), "photo");
        await System.IO.File.WriteAllTextAsync(dir.File("b.jpg"), "photo");
        await System.IO.File.WriteAllTextAsync(dir.File("c.txt"), "photo");
        await System.IO.File.WriteAllTextAsync(dir.File("d.txt"), "photo");
        await System.IO.File.WriteAllBytesAsync(dir.File("empty-a"), []);
        await System.IO.File.WriteAllBytesAsync(dir.File("empty-b"), []);
        await System.IO.File.WriteAllTextAsync(dir.File("Thumbs.db"), "cache");
        Directory.CreateDirectory(dir.File("photos"));
        await System.IO.File.WriteAllTextAsync(dir.File("photos", "Thumbs.db"), "cache");
        Directory.CreateDirectory(dir.File("skipme"));
        await System.IO.File.WriteAllTextAsync(dir.File("skipme", "a.jpg"), "photo");
        await System.IO.File.WriteAllTextAsync(dir.File("skipme", "b.jpg"), "photo");
        Directory.CreateDirectory(dir.File("nested"));
        await System.IO.File.WriteAllTextAsync(dir.File("nested", "a.jpg"), "photo");

        var settings = new ScanSettings { MinSize = 1 };
        settings.Extensions.Add("jpg");
        settings.ExcludeDirectories.Add("skipme");
        var filtered = await Scan(dir.Path, settings);
        var group = Assert.Single(filtered.Groups);
        Assert.Equal(3, group.Files.Count);
        Assert.Equal(2, filtered.SkippedCache);
        Assert.True(filtered.SkippedExtension >= 2);

        settings.Recurse = false;
        var top = await Scan(dir.Path, settings);
        Assert.Equal(2, Assert.Single(top.Groups).Files.Count);

        var emptySettings = new ScanSettings { IncludeEmpty = true };
        var withEmpty = await Scan(dir.Path, emptySettings);
        Assert.Contains(withEmpty.Groups, item => item.Length == 0 && item.Files.Count == 2);
    }

    [Fact]
    public async Task Hard_links_do_not_count_as_extra_space()
    {
        using var dir = new TempDir();
        var original = dir.File("a.bin");
        var link = dir.File("b.bin");
        await System.IO.File.WriteAllBytesAsync(original, [1, 2, 3, 4, 5]);
        Assert.True(CreateHardLink(link, original, IntPtr.Zero), "Could not create a hard link.");

        var result = await Scan(dir.Path);
        var group = Assert.Single(result.Groups);
        Assert.True(group.IsHardLinkOnly);
        Assert.Equal(0, group.ReclaimableBytes);
        Assert.All(group.Files, file => Assert.True(file.LinkCount >= 2));
    }

    static Task<ScanResult> Scan(string path, ScanSettings? settings = null) =>
        DuplicateFinder.ScanAsync([path], settings ?? new ScanSettings(), progress: null, CancellationToken.None);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);
}

public class ActionTests
{
    [Fact]
    public async Task Deletes_extras_and_empty_folders_but_not_the_root()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.File("keep"));
        Directory.CreateDirectory(dir.File("gone", "deep"));
        Directory.CreateDirectory(dir.File("already-empty"));
        await System.IO.File.WriteAllTextAsync(dir.File("keep", "a.txt"), "same");
        await System.IO.File.WriteAllTextAsync(dir.File("gone", "deep", "a.txt"), "same");
        await System.IO.File.WriteAllTextAsync(dir.File("stay.txt"), "other");

        var result = await DuplicateFinder.ScanAsync([dir.Path], new ScanSettings(), null, CancellationToken.None);
        var plan = Planner.Create(result.SpaceGroups, KeepPolicy.ShortestPath, null, includeHardLinkOnly: false);
        Assert.Equal(1, plan.ExtraFiles);

        var predicted = FileActions.PredictEmptyDirectories(result.Roots, plan.Groups.SelectMany(group => group.Extras.Select(file => file.FullPath)).ToList());
        Assert.Contains(predicted, path => path.EndsWith(System.IO.Path.Combine("gone", "deep"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(predicted, path => path.EndsWith("gone", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(predicted, path => PathRules.Same(path, dir.Path));

        var outcome = FileActions.Delete(plan, result.Roots, recycle: false, removeEmptyDirectories: true);
        Assert.Equal(0, outcome.Failed);
        Assert.Equal(1, outcome.Removed);
        Assert.True(System.IO.File.Exists(dir.File("keep", "a.txt")));
        Assert.False(Directory.Exists(dir.File("gone")));
        Assert.True(Directory.Exists(dir.File("already-empty")));
        Assert.True(Directory.Exists(dir.Path));
    }

    [Fact]
    public async Task Move_keeps_one_copy_and_the_relative_path()
    {
        using var dir = new TempDir();
        using var dest = new TempDir();
        await System.IO.File.WriteAllTextAsync(dir.File("a.txt"), "same");
        Directory.CreateDirectory(dir.File("nested"));
        await System.IO.File.WriteAllTextAsync(dir.File("nested", "a.txt"), "same");

        var result = await DuplicateFinder.ScanAsync([dir.Path], new ScanSettings(), null, CancellationToken.None);
        var plan = Planner.Create(result.Groups, KeepPolicy.ShortestPath, null, includeHardLinkOnly: false);
        var batch = System.IO.Path.Combine(dest.Path, "batch");
        var outcome = FileActions.Move(plan, result.Roots, batch, removeEmptyDirectories: true);

        Assert.Equal(0, outcome.Failed);
        Assert.True(System.IO.File.Exists(dir.File("a.txt")));
        Assert.False(System.IO.File.Exists(dir.File("nested", "a.txt")));
        var moved = System.IO.Path.Combine(batch, new DirectoryInfo(dir.Path).Name, "nested", "a.txt");
        Assert.True(System.IO.File.Exists(moved));
        Assert.Equal("same", await System.IO.File.ReadAllTextAsync(moved));
    }

    [Fact]
    public async Task Report_marks_the_keeper_and_writes_a_signature()
    {
        using var dir = new TempDir();
        var olderPath = dir.File("old.txt");
        var newerPath = dir.File("new.txt");
        await System.IO.File.WriteAllTextAsync(olderPath, "same-report");
        await System.IO.File.WriteAllTextAsync(newerPath, "same-report");
        System.IO.File.SetLastWriteTimeUtc(olderPath, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        System.IO.File.SetLastWriteTimeUtc(newerPath, new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var result = await DuplicateFinder.ScanAsync([dir.Path], new ScanSettings(), null, CancellationToken.None);
        var csv = dir.File("report.csv");
        var json = dir.File("report.json");
        ReportWriter.Write(csv, result, KeepPolicy.Newest, overrides: null);
        ReportWriter.Write(json, result, KeepPolicy.Newest, overrides: null);

        var bytes = await System.IO.File.ReadAllBytesAsync(csv);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Contains(Assert.Single(result.Groups).Signature, text);
        Assert.Contains("true", text);

        using var document = JsonDocument.Parse(await System.IO.File.ReadAllTextAsync(json));
        var files = document.RootElement.GetProperty("groups")[0].GetProperty("files");
        var keeper = files.EnumerateArray().Single(file => file.GetProperty("keeper").GetBoolean());
        Assert.EndsWith("new.txt", keeper.GetProperty("path").GetString(), StringComparison.OrdinalIgnoreCase);
    }
}
