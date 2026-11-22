using DuplicateTracker;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            if (cancel.IsCancellationRequested)
                return;

            eventArgs.Cancel = true;
            cancel.Cancel();
            Console.WriteLine();
            Console.Error.WriteLine("Stopping. Press Ctrl+C again to quit now.");
        };

        SynchronizationContext.SetSynchronizationContext(null);
        ConsoleUi.Initialize();
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                ConsoleUi.Error("DuplicateTracker runs on Windows.");
                return ExitCodes.Usage;
            }

            return App.Run(args, cancel.Token);
        }
        catch (OperationCanceledException)
        {
            ConsoleUi.Line("Stopped.");
            return ExitCodes.Cancelled;
        }
        finally
        {
            ConsoleUi.Restore();
        }
    }
}
