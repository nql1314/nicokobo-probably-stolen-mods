using System.Diagnostics;
using System.Text.Json;

internal static class PerfCounters
{
    internal static long Finds, Items, Ownership;
    internal static readonly List<object> Results = new();
    internal static int Checks;
    internal static List<GameItem> Capture(List<GameItem> items)
    { Finds++; Items += items.Count; return items; }
    internal static bool Owned(bool value) { Ownership++; return value; }
    internal static void Check(bool condition, string message)
    { Checks++; if (!condition) throw new InvalidOperationException(message); }
    internal static void Measure(string name, int items, Action operation)
    {
        const int samples = 51, iterations = 8;
        for (int index = 0; index < 64; index++) operation();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Finds = Items = Ownership = 0;
        long allocation = GC.GetAllocatedBytesForCurrentThread();
        var times = new double[samples];
        for (int sample = 0; sample < samples; sample++)
        {
            long before = Stopwatch.GetTimestamp();
            for (int iteration = 0; iteration < iterations; iteration++) operation();
            times[sample] = (Stopwatch.GetTimestamp() - before) * 1000d / Stopwatch.Frequency / iterations;
        }
        allocation = GC.GetAllocatedBytesForCurrentThread() - allocation;
        Array.Sort(times);
        double count = samples * iterations;
        Results.Add(new { scenario = name, inventoryItems = items, calls = (int)count,
            p50Milliseconds = times[samples / 2], p95Milliseconds = times[(int)Math.Floor(samples * .95)],
            bytesPerCall = allocation / count, inventoryScansPerCall = Finds / count,
            returnedItemsPerCall = Items / count, ownershipChecksPerCall = Ownership / count });
    }
    internal static void Save(string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(new {
            environment = new { runtime = Environment.Version.ToString(), os = Environment.OSVersion.ToString(),
                processorCount = Environment.ProcessorCount, process64Bit = Environment.Is64BitProcess },
            boundary = "Actual production source, existing headless inventory ports with counters. Timings and bytes are not Unity/Mono native gameplay measurements.",
            checks = Checks, results = Results
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS: {Checks} immediate-state checks; {Results.Count} source-linked benchmark scenarios; {path}");
    }
}
