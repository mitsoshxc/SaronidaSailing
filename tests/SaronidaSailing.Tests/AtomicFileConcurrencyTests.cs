using System.Text.RegularExpressions;
using NLog;
using NLog.Targets;

namespace SaronidaSailing.Tests;

public class AtomicFileConcurrencyTests : IDisposable
{
    private const int LinesPerWriter = 2000;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "saronida-atomic-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task TwoIndependentWriters_OnOneFile_LoseNoLinesAndNeverInterleave()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "shared.log");
        var payload = new string('x', 200);

        await Task.WhenAll(
            Task.Run(() => Write(path, "pod-a", payload)),
            Task.Run(() => Write(path, "pod-b", payload)));

        string[] lines;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream))
        {
            lines = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        Assert.Equal(2 * LinesPerWriter, lines.Length);
        var format = new Regex($@"^(pod-a|pod-b)\|(\d+)\|{payload}$");
        var matches = lines.Select(l => format.Match(l)).ToArray();
        Assert.All(matches, m => Assert.True(m.Success));
        foreach (var pod in new[] { "pod-a", "pod-b" })
        {
            var numbers = matches.Where(m => m.Groups[1].Value == pod).Select(m => int.Parse(m.Groups[2].Value)).ToArray();
            Assert.Equal(Enumerable.Range(0, LinesPerWriter), numbers);
        }
    }

    private static void Write(string path, string instance, string payload)
    {
        var target = new AtomicFileTarget()
        {
            Name = "atomic-" + instance,
            FileName = path,
            Layout = instance + "|${event-properties:item=n}|${message}",
            ConcurrentWrites = true,
            KeepFileOpen = true,
        };
        using var factory = new LogFactory();
        factory.Setup().LoadConfiguration(b => b.ForLogger().WriteTo(target));
        var logger = factory.GetLogger("Saronida.Tests.Atomic");

        for (var i = 0; i < LinesPerWriter; i++)
        {
            logger.WithProperty("n", i).Info(payload);
        }

        factory.Flush();
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}
