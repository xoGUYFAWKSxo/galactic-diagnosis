using GalacticDiagnosis.Core;

namespace GalacticDiagnosis.Tests;

public class CollectorRunnerTests
{
    sealed class Fake(string id, Func<CancellationToken, SectionResult> collect, int timeoutMs = 5000) : ICollector
    {
        public string Id => id;
        public string Title => id;
        public TimeSpan Timeout => TimeSpan.FromMilliseconds(timeoutMs);
        public SectionResult Collect(CancellationToken ct) => collect(ct);
    }

    static SectionResult Ok(string id) => new() { Id = id, Title = id, Verdict = Verdict.Good, Summary = "ok" };

    [Fact]
    public async Task Results_come_back_in_input_order_and_report_each_one()
    {
        var done = new List<string>();
        var results = await CollectorRunner.RunAllAsync(
            [new Fake("slow", _ => { Thread.Sleep(150); return Ok("slow"); }), new Fake("fast", _ => Ok("fast"))],
            isAdmin: true, s => { lock (done) done.Add(s.Id); });
        Assert.Equal(["slow", "fast"], results.Select(r => r.Id));
        Assert.Equal(["fast", "slow"], done);
    }

    [Fact]
    public async Task Failures_and_timeouts_become_unavailable()
    {
        var results = await CollectorRunner.RunAllAsync(
            [new Fake("boom", _ => throw new InvalidOperationException("x")), new Fake("hang", _ => { Thread.Sleep(2000); return Ok("hang"); }, timeoutMs: 100)],
            isAdmin: false);
        Assert.All(results, r => Assert.Equal(Verdict.Unavailable, r.Verdict));
        Assert.Contains("took longer", results[1].Summary);
    }

    [Fact]
    public async Task Cancel_stops_the_run()
    {
        using var cts = new CancellationTokenSource(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CollectorRunner.RunAllAsync(
            [new Fake("hang", ct => { ct.WaitHandle.WaitOne(3000); return Ok("hang"); })], isAdmin: true, ct: cts.Token));
    }
}
