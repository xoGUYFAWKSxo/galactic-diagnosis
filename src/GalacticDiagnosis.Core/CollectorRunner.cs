using System.Diagnostics;
using System.Security.Principal;

namespace GalacticDiagnosis.Core;

public static class CollectorRunner
{
    public static bool IsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Runs every collector in parallel. Results come back in input order. Only cancellation throws.</summary>
    public static async Task<List<SectionResult>> RunAllAsync(
        IReadOnlyList<ICollector> collectors, bool isAdmin, Action<SectionResult>? onDone = null, CancellationToken ct = default)
    {
        var tasks = collectors.Select(c => RunOneAsync(c, isAdmin, onDone, ct));
        return [.. await Task.WhenAll(tasks)];
    }

    static async Task<SectionResult> RunOneAsync(ICollector c, bool isAdmin, Action<SectionResult>? onDone, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(c.Timeout);
        SectionResult result;
        try
        {
            // LongRunning gives each collector its own thread so a hung WMI call can't starve the others.
            var work = Task.Factory.StartNew(() => c.Collect(cts.Token), cts.Token,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
            result = await work.WaitAsync(c.Timeout, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException)
        {
            result = SectionResult.Unavailable(c.Id, c.Title,
                $"Couldn't read this section: it took longer than {c.Timeout.TotalSeconds:0} seconds.");
        }
        catch (Exception ex)
        {
            var why = c.NeedsAdmin && !isAdmin
                ? "Couldn't read this section. Run the tool as administrator."
                : "Couldn't read this section on this PC.";
            result = SectionResult.Unavailable(c.Id, c.Title, why, $"{ex.GetType().Name}: {ex.Message}");
        }
        result.ElapsedMs = Math.Round(sw.Elapsed.TotalMilliseconds);
        onDone?.Invoke(result);
        return result;
    }
}
