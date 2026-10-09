namespace InterviewToolkit.Concurrency;

/// <summary>A critical section protects a whole read/modify/write operation.</summary>
public sealed class LockedCounter
{
    private readonly Lock _gate = new(); // .NET 9+; lock(object) works on older versions.
    private int _value;
    public void Increment() { lock (_gate) { _value++; } }
    public int Value { get { lock (_gate) { return _value; } } }
}

/// <summary>Use atomics when the invariant involves just one simple operation.</summary>
public sealed class AtomicCounter
{
    private int _value;
    public void Increment() => Interlocked.Increment(ref _value);
    public int Value => Volatile.Read(ref _value);
}

/// <summary>A one-time atomic claim. A successful claim does not imply its work is finished.</summary>
public sealed class AtomicClaim
{
    private int _claimed;
    public bool TryClaim() => Interlocked.CompareExchange(ref _claimed, 1, 0) == 0;
}

/// <summary>Single publication using release/acquire ordering; never reset or republish.</summary>
public static class PublicationExample
{
    public static async Task<int> SpinWaitAsync()
    {
        int ready = 0, value = 0;
        var writer = Task.Run(() =>
        {
            value = 42;
            Volatile.Write(ref ready, 1);
        });
        bool published = SpinWait.SpinUntil(() => Volatile.Read(ref ready) == 1,
            TimeSpan.FromSeconds(5));
        // Read before awaiting writer to demonstrate the flag's publication protocol.
        int result = published ? value : 0;
        await writer;
        if (!published) throw new TimeoutException();
        return result;
    }
}

/// <summary>Condition-variable example: wait until an item exists or the queue completes.</summary>
public sealed class MonitorQueue<T>
{
    private readonly object _gate = new(); // Monitor requires an object monitor, not Lock.EnterScope.
    private readonly Queue<T> _items = new();
    private bool _completed;

    public void Enqueue(T item)
    {
        lock (_gate)
        {
            if (_completed) throw new InvalidOperationException("Queue is complete.");
            _items.Enqueue(item);
            Monitor.PulseAll(_gate);
        }
    }

    public bool TryDequeue(out T item)
    {
        lock (_gate)
        {
            // Wait releases the monitor; reacquires it before returning.
            // Another consumer might win the item, so always recheck in a loop.
            while (_items.Count == 0 && !_completed) Monitor.Wait(_gate);
            if (_items.Count == 0) { item = default!; return false; }
            item = _items.Dequeue();
            return true;
        }
    }

    public void Complete()
    {
        lock (_gate) { _completed = true; Monitor.PulseAll(_gate); }
    }
}

/// <summary>Readers may overlap; a writer excludes all readers and writers.</summary>
public sealed class ReadMostlyCache : IDisposable
{
    private readonly ReaderWriterLockSlim _gate = new();
    private readonly Dictionary<string, string> _values = new();
    public string? Get(string key)
    {
        _gate.EnterReadLock();
        try { return _values.GetValueOrDefault(key); }
        finally { _gate.ExitReadLock(); }
    }
    public void Set(string key, string value)
    {
        _gate.EnterWriteLock();
        try { _values[key] = value; }
        finally { _gate.ExitWriteLock(); }
    }
    // Dispose only after every caller has finished using this instance.
    public void Dispose() => _gate.Dispose();
}

/// <summary>Educational spin lock. Prefer lock unless measurement justifies spinning.</summary>
public sealed class SpinCounter
{
    // SpinLock is a mutable struct: never make this readonly or copy it.
    private SpinLock _gate = new(enableThreadOwnerTracking: true);
    private int _value;
    public void Increment()
    {
        bool taken = false;
        try { _gate.Enter(ref taken); _value++; }
        finally { if (taken) _gate.Exit(); }
    }
    public int Value => Volatile.Read(ref _value);
}

/// <summary>Mutexes are thread-owned; keep the protected action synchronous.</summary>
public static class MutexExample
{
    public static void RunExclusive(string name, Action action)
    {
        using var mutex = new Mutex(false, name);
        bool acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException)
            {
                // Ownership was granted, but previous owner died. Real applications
                // must validate/repair shared state before using it.
                acquired = true;
                throw;
            }
            if (!acquired) throw new TimeoutException("Mutex was not available.");
            action();
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }
}
