namespace InterviewToolkit.Concurrency;

public static class ThreadingExamples
{
    public static int DedicatedThread()
    {
        int result = 0;
        var worker = new Thread(() => result = 6 * 7);
        worker.Start();
        worker.Join(); // Blocks caller and makes completed worker writes visible.
        return result;
    }

    public static Task<int> ThreadPoolWorkAsync()
    {
        // A raw pool callback has no Task/exception propagation of its own.
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { completion.SetResult(6 * 7); }
            catch (Exception error) { completion.SetException(error); }
        });
        return completion.Task;
    }

    public static Task<long> CpuWorkAsync(int count, CancellationToken token = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return Task.Run(() =>
        {
            long sum = 0;
            for (int i = 0; i < count; i++)
            {
                token.ThrowIfCancellationRequested(); // Token on Task.Run alone cannot stop running code.
                sum += i;
            }
            return sum;
        }, token);
    }

    // Simulates waiting on I/O without occupying a sleeping thread.
    public static async Task<int> SimulatedIoAsync(int id, CancellationToken token = default)
    {
        await Task.Delay(10, token);
        return checked(id * id);
    }

    public static Task<int[]> WhenAllAsync(CancellationToken token = default) =>
        Task.WhenAll(Enumerable.Range(1, 4).Select(id => SimulatedIoAsync(id, token)));

    public static async Task<int> WhenAnyAsync(CancellationToken token = default)
    {
        Task<int>[] tasks = [SimulatedIoAsync(2, token), SimulatedIoAsync(3, token)];
        Task<int> first = await Task.WhenAny(tasks);
        // WhenAny neither cancels nor observes the other tasks. Drain both.
        await Task.WhenAll(tasks);
        return await first;
    }

    public static async Task CancellableWorkerAsync(Action onStarted, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        onStarted();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            await Task.Delay(10, token); // Representative cancellable I/O/wait.
        }
    }
}
