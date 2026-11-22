namespace DuplicateTracker;

internal static class App
{
    public static int Run(string[] args, CancellationToken cancellationToken)
    {
        AppCommand command;
        try
        {
            command = CommandLine.Parse(args);
        }
        catch (CommandException ex)
        {
            ConsoleUi.Error(ex.Message);
            ConsoleUi.Error("Run DuplicateTracker --help for usage.");
            return ExitCodes.Usage;
        }

        try
        {
            return command.Kind switch
            {
                AppCommandKind.Help => Help(),
                AppCommandKind.Version => Version(),
                AppCommandKind.Hash => Hash(command.HashPath!, cancellationToken),
                AppCommandKind.Interactive => Session.Interactive(cancellationToken),
                _ => RunScan(command, cancellationToken)
            };
        }
        catch (CommandException ex)
        {
            ConsoleUi.Error(ex.Message);
            return ExitCodes.Usage;
        }
        catch (IOException ex)
        {
            ConsoleUi.Error(ex.Message);
            return ExitCodes.ActionFailed;
        }
    }

    public static int RunScan(AppCommand command, CancellationToken cancellationToken)
    {
        if (!command.Quiet)
            ConsoleUi.PrintLogo();

        var (roots, dropped) = PathRules.NormalizeRoots(command.Paths);
        foreach (var extra in dropped)
            ConsoleUi.Line("Not scanning " + extra + " on its own. It is inside another folder you chose.", Tone.Dim);

        if (!command.Quiet)
        {
            ConsoleUi.Line("");
            ConsoleUi.Line("  " + SettingsText.Brief(command.Settings), Tone.Dim);
        }

        var progress = command.Quiet || Console.IsOutputRedirected ? null : new ScanProgressDisplay();
        if (progress is not null)
            ConsoleUi.HideCursor();

        ScanResult result;
        try
        {
            result = DuplicateFinder.ScanAsync(roots, command.Settings, progress, cancellationToken).GetAwaiter().GetResult();
        }
        finally
        {
            progress?.Complete();
            ConsoleUi.ShowCursor();
        }

        if (!command.Quiet || result.DuplicatesFound || result.ReadErrors > 0)
            Presentation.PrintSummary(result);

        Presentation.PrintIssues(result);

        if (command.ExportPath is not null)
        {
            ReportWriter.Write(command.ExportPath, result, command.Keep, overrides: null);
            ConsoleUi.Line("Report written to " + Path.GetFullPath(command.ExportPath), Tone.Good);
        }

        if (command.Delete || command.MoveTo is not null)
            return Apply(command, result, cancellationToken);

        if (command.ReviewAfter)
            return Review.Run(result, command, cancellationToken);

        return ExitCodes.For(result, action: null, command.FailOnDuplicates);
    }

    public static void PrintHash(string path, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        var signature = DuplicateFinder.HashFileAsync(path, cancellationToken).GetAwaiter().GetResult();
        ConsoleUi.Line("");
        ConsoleUi.Line("  SHA-256  " + signature, Tone.Accent);
        ConsoleUi.Line("  Size     " + ByteSize.Format(info.Length));
        ConsoleUi.Line("  Path     " + Path.GetFullPath(path));
    }

    static int Apply(AppCommand command, ScanResult result, CancellationToken cancellationToken)
    {
        var plan = Planner.Create(result.SpaceGroups, command.Keep, overrides: null, includeHardLinkOnly: false);
        var skippedLinks = result.HardLinkGroups.Count();
        if (plan.ExtraFiles == 0)
        {
            ConsoleUi.Line(command.MoveTo is null
                ? "No extra copies that take space."
                : "No extra copies to move.");
            if (skippedLinks > 0)
                ConsoleUi.Line($"{skippedLinks.ToString("N0", CultureInfo.InvariantCulture)} hard-link groups were left out.", Tone.Dim);

            return ExitCodes.For(result, action: null, command.FailOnDuplicates);
        }

        string? batch = null;
        if (command.MoveTo is not null)
            batch = FileActions.BatchFolder(command.MoveTo);

        if (skippedLinks > 0)
            ConsoleUi.Line($"{skippedLinks.ToString("N0", CultureInfo.InvariantCulture)} hard-link groups are not in this plan.", Tone.Dim);

        Presentation.PrintPlan(plan, command.Keep, command.MoveTo is null ? "delete" : "move", batch);

        var console = !Console.IsInputRedirected;
        if (command.DryRun || (!command.Yes && !console))
        {
            ConsoleUi.Line("Nothing was changed.");
            if (!command.DryRun)
                ConsoleUi.Line("Pass --yes to apply this plan.");

            return ExitCodes.For(result, action: null, command.FailOnDuplicates);
        }

        if (!command.Yes && !ConsoleUi.AskYesNo(command.MoveTo is null ? "Remove these extra files?" : "Move these files?", false, cancellationToken))
        {
            ConsoleUi.Line("Nothing was changed.");
            return ExitCodes.For(result, action: null, command.FailOnDuplicates);
        }

        var recycle = true;
        if (command.Delete)
        {
            var style = command.DeleteStyle;
            if (style == DeleteStyle.Ask)
                style = command.Yes || !console ? DeleteStyle.Recycle : Prompts.AskDeleteStyle(cancellationToken);

            if (style == DeleteStyle.Permanent && console && !command.Yes && !Prompts.ConfirmPermanent(cancellationToken))
            {
                ConsoleUi.Line("Nothing was changed.");
                return ExitCodes.For(result, action: null, command.FailOnDuplicates);
            }

            recycle = style != DeleteStyle.Permanent;
        }

        var removeEmpty = command.RemoveEmptyDirs;
        if (!removeEmpty && console && !command.Yes)
        {
            var paths = plan.Groups.SelectMany(group => group.Extras.Select(file => file.FullPath)).ToList();
            removeEmpty = Prompts.AskEmpty(FileActions.PredictEmptyDirectories(result.Roots, paths), cancellationToken);
        }

        var outcome = command.MoveTo is not null
            ? FileActions.Move(plan, result.Roots, batch!, removeEmpty)
            : FileActions.Delete(plan, result.Roots, recycle, removeEmpty);

        Presentation.PrintOutcome(outcome, command.MoveTo is not null, recycle, removeEmpty, batch);
        FileActions.ForgetRemoved(result.Groups, outcome.RemovedPaths);
        return ExitCodes.For(result, outcome, command.FailOnDuplicates);
    }

    static int Help()
    {
        ConsoleUi.Line(CommandLine.Help());
        return ExitCodes.Ok;
    }

    static int Version()
    {
        ConsoleUi.Line(AppInfo.Name + " " + AppInfo.Version);
        return ExitCodes.Ok;
    }

    static int Hash(string path, CancellationToken cancellationToken)
    {
        try
        {
            PrintHash(path, cancellationToken);
            return ExitCodes.Ok;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            ConsoleUi.Error(ex.Message);
            return ExitCodes.ReadErrors;
        }
    }
}
