namespace InterviewToolkit.Concurrency;

/// <summary>Executable examples with result checks; no assumption about worker execution order.</summary>
public static class ConcurrencyTour
{
    public static async Task RunAsync()
    {
        Console.WriteLine("=== Concurrency ===");
        var locked = new LockedCounter();
        var atomic = new AtomicCounter();
        var spin = new SpinCounter();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 1_000; i++)
            {
                locked.Increment(); atomic.Increment(); spin.Increment();
            }
        })));
        Check(locked.Value == 4_000 && atomic.Value == 4_000 && spin.Value == 4_000,
            "lock, Interlocked and SpinLock prevent lost updates");
        var claim = new AtomicClaim();
        int winners = 0;
        Parallel.For(0, 100, _ => { if (claim.TryClaim()) Interlocked.Increment(ref winners); });
        Check(winners == 1, "compare-and-swap grants one claim");
        Check(await PublicationExample.SpinWaitAsync() == 42, "Volatile flag publishes data to SpinWait reader");

        var queue = new MonitorQueue<int>();
        var consumer = Task.Run(() =>
        {
            int total = 0;
            while (queue.TryDequeue(out int item)) total += item;
            return total;
        });
        for (int i = 1; i <= 5; i++) queue.Enqueue(i);
        queue.Complete();
        Check(await consumer == 15, "Monitor queue drains and terminates on completion");
        bool rejected = false;
        try { queue.Enqueue(6); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "completed queue rejects new items");

        using (var cache = new ReadMostlyCache())
        {
            cache.Set("answer", "42");
            var reads = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => cache.Get("answer"))));
            Check(reads.All(value => value == "42"), "reader/writer cache");
        }
        int exclusive = 0;
        string mutexName = $"InterviewToolkit_{Guid.NewGuid():N}";
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            MutexExample.RunExclusive(mutexName, () => exclusive++))));
        Check(exclusive == 4, "named Mutex excludes competing owners");

        using (var limiter = new AsyncConcurrencyLimiter(2))
        {
            var bothEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var unblock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int active = 0, entered = 0;
            int peak = 0;
            var peakGate = new object();
            var tasks = Enumerable.Range(0, 6).Select(id => limiter.RunAsync(async token =>
            {
                int current = Interlocked.Increment(ref active);
                lock (peakGate) peak = Math.Max(peak, current);
                if (Interlocked.Increment(ref entered) == 2) bothEntered.TrySetResult();
                try { await unblock.Task.WaitAsync(token); return id; }
                finally { Interlocked.Decrement(ref active); }
            })).ToArray();
            try
            {
                await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                using var canceled = new CancellationTokenSource();
                canceled.Cancel();
                await ExpectCanceledAsync(() => limiter.RunAsync(_ => Task.FromResult(-1), canceled.Token),
                    canceled.Token);
                Check(Volatile.Read(ref entered) == 2, "semaphore holds extra callers and cancels a waiting caller");
            }
            finally
            {
                unblock.TrySetResult();
                // Always finish users before disposing their semaphore.
                await Task.WhenAll(tasks);
            }
            Check(peak == 2 && active == 0, "async semaphore respects capacity and releases permits");
            try { await limiter.RunAsync<int>(_ => Task.FromException<int>(new InvalidOperationException("sample"))); }
            catch (InvalidOperationException) { }
            Check(await limiter.RunAsync(_ => Task.FromResult(42)) == 42, "failed operation releases semaphore permit");
        }

        Check(await CoordinationExamples.SemaphoreAsync() == 6, "OS semaphore workers complete");
        Check(await CoordinationExamples.AutoResetAsync() == 42, "auto-reset signal");
        Check((await CoordinationExamples.ManualResetAsync()).SequenceEqual([1, 2, 3]), "manual-reset gate");
        Check(await CoordinationExamples.CountdownAsync() == 3, "countdown reaches zero");
        Check(CoordinationExamples.BarrierPhases() == 2, "barrier reuses two phases");
        Check((await CoordinationExamples.ChannelPipelineAsync()).SequenceEqual([1, 4, 9, 16, 25]),
            "bounded channel preserves work and completes");
        Check((await CoordinationExamples.BlockingPipelineAsync()).SequenceEqual([1, 4, 9, 16, 25]),
            "blocking producer/consumer completes and drains");

        Check(ThreadingExamples.DedicatedThread() == 42, "Thread Start/Join");
        Check(await ThreadingExamples.ThreadPoolWorkAsync() == 42, "thread pool completion");
        Check(await ThreadingExamples.CpuWorkAsync(100) == 4_950, "Task.Run CPU result");
        Check((await ThreadingExamples.WhenAllAsync()).SequenceEqual([1, 4, 9, 16]), "WhenAll keeps input order");
        Check(await ThreadingExamples.WhenAnyAsync() is 4 or 9, "WhenAny handles either winner and drains remaining work");

        using (var stop = new CancellationTokenSource())
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task worker = ThreadingExamples.CancellableWorkerAsync(() => started.SetResult(), stop.Token);
            await started.Task;
            stop.Cancel();
            await ExpectCanceledAsync(() => worker, stop.Token);
            Check(worker.IsCanceled, "running worker cooperatively cancels");
        }

        int[] values = [1, 2, 3, 4];
        Check(ParallelExamples.ForSquares(values).SequenceEqual([1, 4, 9, 16]), "Parallel.For distinct output slots");
        Check(ParallelExamples.ForEachSum(values) == 10, "Parallel.ForEach local reduction");
        Check(ParallelExamples.InvokeIndependent().SequenceEqual([4, 9]), "Parallel.Invoke");
        Check(ParallelExamples.PlinqSquares(values).SequenceEqual([1, 4, 9, 16]), "ordered PLINQ");
        var squares = await ParallelExamples.ForEachAsyncSquares(values);
        Check(values.All(id => squares[id] == id * id), "bounded Parallel.ForEachAsync");
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            await ExpectCanceledAsync(() => ParallelExamples.ForEachAsyncSquares(values, canceled.Token), canceled.Token);
            await ExpectCanceledAsync(() => CoordinationExamples.ChannelPipelineAsync(canceled.Token), canceled.Token);
            await ExpectCanceledAsync(() => ThreadingExamples.CpuWorkAsync(100, canceled.Token), canceled.Token);
            await ExpectCanceledAsync(() => { ParallelExamples.ForSquares(values, canceled.Token); return Task.CompletedTask; }, canceled.Token);
            await ExpectCanceledAsync(() => { ParallelExamples.ForEachSum(values, canceled.Token); return Task.CompletedTask; }, canceled.Token);
        }
        Console.WriteLine("  cancellation propagated through tasks, channel and parallel loops: passed");
        Console.WriteLine();
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException($"Concurrency check failed: {label}");
        Console.WriteLine($"  {label}: passed");
    }

    private static async Task ExpectCanceledAsync(Func<Task> work, CancellationToken token)
    {
        try { await work(); }
        catch (OperationCanceledException error) when (token.IsCancellationRequested && error.CancellationToken == token)
        { return; }
        throw new InvalidOperationException("Expected cancellation with the supplied token.");
    }
}
