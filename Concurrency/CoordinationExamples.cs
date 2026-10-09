using System.Threading.Channels;
using System.Collections.Concurrent;

namespace InterviewToolkit.Concurrency;

/// <summary>A counting semaphore bounds concurrent async operations.</summary>
public sealed class AsyncConcurrencyLimiter : IDisposable
{
    private readonly SemaphoreSlim _slots;
    public AsyncConcurrencyLimiter(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _slots = new SemaphoreSlim(capacity, capacity);
    }
    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work,
        CancellationToken token = default)
    {
        await _slots.WaitAsync(token);
        // Enter try only AFTER acquisition; a canceled wait owns no permit.
        try { return await work(token); }
        finally { _slots.Release(); }
    }
    public void Dispose() => _slots.Dispose(); // Only after all operations finish.
}

public static class CoordinationExamples
{
    public static async Task<int[]> BlockingPipelineAsync(CancellationToken token = default)
    {
        using var queue = new BlockingCollection<int>(boundedCapacity: 2);
        var consumer = Task.Run(() =>
            queue.GetConsumingEnumerable(token).Select(item => item * item).ToArray(), token);
        try
        {
            for (int i = 1; i <= 5; i++) queue.Add(i, token);
        }
        finally
        {
            queue.CompleteAdding();
            // Join before disposing even if producer cancellation/failure occurred.
            await consumer;
        }
        return await consumer;
    }

    public static async Task<int> SemaphoreAsync()
    {
        using var slots = new Semaphore(2, 2);
        int completed = 0;
        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() =>
        {
            slots.WaitOne(); // Blocks a thread; use SemaphoreSlim for async waits.
            try { Interlocked.Increment(ref completed); }
            finally { slots.Release(); }
        })));
        return completed;
    }

    public static async Task<int> AutoResetAsync()
    {
        using var ready = new AutoResetEvent(false);
        var worker = Task.Run(() =>
        {
            if (!ready.WaitOne(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            return 42;
        });
        ready.Set(); // Remembers one signal even if worker has not started yet.
        return await worker;
    }

    public static async Task<int[]> ManualResetAsync()
    {
        using var ready = new ManualResetEventSlim(false);
        var workers = Enumerable.Range(1, 3).Select(id => Task.Run(() =>
        {
            if (!ready.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            return id;
        })).ToArray();
        ready.Set(); // Opens the gate for all current and future waiters.
        return await Task.WhenAll(workers);
    }

    public static async Task<int> CountdownAsync()
    {
        using var done = new CountdownEvent(3);
        int completed = 0;
        var workers = Enumerable.Range(0, 3).Select(_ => Task.Run(() =>
        {
            try { Interlocked.Increment(ref completed); }
            finally { done.Signal(); }
        })).ToArray();
        bool signaled = done.Wait(TimeSpan.FromSeconds(5));
        await Task.WhenAll(workers); // Also observes worker exceptions before disposal.
        if (!signaled) throw new TimeoutException();
        return completed;
    }

    public static int BarrierPhases()
    {
        int phases = 0;
        using var barrier = new Barrier(2, _ => Interlocked.Increment(ref phases));
        // Dedicated threads avoid starving the pool with workers waiting for peers.
        Exception?[] errors = new Exception?[2];
        var workers = Enumerable.Range(0, 2).Select(id => new Thread(() =>
        {
            try
            {
                for (int phase = 0; phase < 2; phase++)
                    if (!barrier.SignalAndWait(TimeSpan.FromSeconds(5)))
                        throw new TimeoutException("Missing barrier participant.");
            }
            catch (Exception error) { errors[id] = error; }
        })).ToArray();
        foreach (var worker in workers) worker.Start();
        foreach (var worker in workers) worker.Join();
        if (errors.Any(error => error is not null))
            throw new AggregateException(errors.OfType<Exception>());
        return phases;
    }

    public static async Task<int[]> ChannelPipelineAsync(CancellationToken token = default)
    {
        var channel = Channel.CreateBounded<int>(new BoundedChannelOptions(2)
        {
            SingleWriter = true, SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait // Backpressure instead of dropping work.
        });
        async Task ProduceAsync()
        {
            Exception? failure = null;
            try
            {
                for (int i = 1; i <= 5; i++) await channel.Writer.WriteAsync(i, token);
            }
            catch (Exception error) { failure = error; throw; }
            finally { channel.Writer.TryComplete(failure); }
        }
        async Task<int[]> ConsumeAsync()
        {
            var results = new List<int>(); // Owned by the single consumer.
            await foreach (int item in channel.Reader.ReadAllAsync(token)) results.Add(item * item);
            return results.ToArray();
        }
        Task producer = ProduceAsync();
        Task<int[]> consumer = ConsumeAsync();
        await Task.WhenAll(producer, consumer);
        return await consumer;
    }
}
