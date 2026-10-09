using System.Collections.Concurrent;

namespace InterviewToolkit.Concurrency;

public static class ParallelExamples
{
    public static int[] ForSquares(int[] values, CancellationToken token = default)
    {
        var results = new int[values.Length];
        Parallel.For(0, values.Length, Options(token), i =>
        {
            token.ThrowIfCancellationRequested();
            results[i] = checked(values[i] * values[i]); // Each index has one writer.
        });
        return results;
    }

    public static long ForEachSum(int[] values, CancellationToken token = default)
    {
        long total = 0;
        // Per-worker accumulation avoids contending on total for every element.
        Parallel.ForEach<int, long>(values, Options(token), () => 0L,
            (value, state, local) => { token.ThrowIfCancellationRequested(); return local + value; },
            local => Interlocked.Add(ref total, local));
        return total;
    }

    public static int[] InvokeIndependent()
    {
        int left = 0, right = 0;
        Parallel.Invoke(() => left = 2 * 2, () => right = 3 * 3);
        return [left, right]; // Invoke waits for both delegates.
    }

    public static int[] PlinqSquares(int[] values, CancellationToken token = default) =>
        values.AsParallel().AsOrdered().WithCancellation(token)
            .Select(value => checked(value * value)).ToArray();

    public static async Task<IReadOnlyDictionary<int, int>> ForEachAsyncSquares(
        IEnumerable<int> ids, CancellationToken token = default)
    {
        var results = new ConcurrentDictionary<int, int>();
        await Parallel.ForEachAsync(ids, new ParallelOptions
        {
            MaxDegreeOfParallelism = 3, CancellationToken = token
        }, async (id, iterationToken) =>
        {
            results[id] = await ThreadingExamples.SimulatedIoAsync(id, iterationToken);
        });
        return results;
    }

    private static ParallelOptions Options(CancellationToken token) => new()
    {
        MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount), CancellationToken = token
    };
}
