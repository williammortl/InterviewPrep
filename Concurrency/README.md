# Concurrency in C#: from threads to parallel loops

This guide targets this project's **.NET 10** environment. The accompanying
classes use the namespace `InterviewToolkit.Concurrency`. Run `dotnet run` from
the project root to execute the concurrency tour along with the existing examples.
Snippets below are independent fragments, generally inside a method; assume
`System.Threading`, `System.Threading.Tasks`, `System.Linq`, and
`InterviewToolkit.Concurrency` are imported. Additional imports are shown as needed.

## 1. The basic model

A **process** is a running application with its own address space. A **thread**
is an execution path inside that process; threads share heap objects but each
has its own stack. The operating system schedules threads onto CPU cores.
Switching between threads costs time, and creating more threads does not create
more cores.

**Concurrency** means operations make progress during overlapping time periods.
**Parallelism** means operations execute at the same instant, usually on separate
cores. **Asynchrony** lets an operation suspend while waiting, allowing its caller
to do other work. An asynchronous I/O operation need not occupy a worker thread
while waiting. `async` does not automatically create a thread or make CPU code parallel.

A **task** represents an operation's eventual completion, result, exception, or
cancellation. It is not a dedicated thread. A task might represent queued CPU
work, a network response, a timer, or a manually supplied completion signal.

### Why shared state causes bugs

`count++` reads the current value, adds one, and writes the result. Two threads can
both read 10 and both write 11: one increment disappears. This is a **race
condition**, here a lost update. Memory visibility also matters: one thread's writes
must be published through appropriate synchronization before another relies on them.

A **critical section** is the code that accesses a shared resource or maintains
an invariant. For example, transferring money requires protecting both the
withdrawal and deposit as a unit. A lock protects a section; it does not magically
make every access to an object safe. Every conflicting access must follow the same
protocol and use the same lock instance.

Prefer independent data, immutable snapshots, or one owner with message passing
before introducing shared mutable objects.

## 2. Synchronization concepts and their C# equivalents

| Computer science concept | How it works | Typical use case | C#/.NET equivalent |
| --- | --- | --- | --- |
| Critical section / mutual exclusion | One owner enters protected code; contenders wait until it leaves | Update a balance and related accounting fields together | `lock` with `Lock` or a private `object`; `Monitor.Enter/Exit` |
| Mutex | Exclusive ownership, potentially coordinated by the OS across processes | Two applications sharing a resource | `Mutex`; use a common name for cross-process ownership |
| Counting semaphore | Maintain N permits; acquisition consumes one, release returns one | At most 3 requests or database operations in flight | `SemaphoreSlim`; `Semaphore` for OS wait handles |
| Binary semaphore | Counting semaphore with maximum count 1; has no thread ownership | Serialize an asynchronous operation across `await` | `SemaphoreSlim(1, 1)` |
| Monitor / condition variable | Protect a predicate with a lock; waiting releases the lock, signaling wakes waiters to check it again | Consumer waits until a queue contains data | `Monitor.Wait/Pulse/PulseAll` with `lock(object)` |
| Reader/writer lock | Multiple readers coexist; a writer needs exclusive access | Cache read frequently and updated occasionally | `ReaderWriterLockSlim` |
| Spin lock | Repeatedly attempt acquisition rather than immediately sleeping | Extremely short, measured low-level critical sections | `SpinLock` |
| Atomic operation / compare-and-swap | Indivisible update of one location; CAS updates only if expected value matches | Counters, claiming an initialization state | `Interlocked` |
| Memory ordering / publication | Order accesses and publish data so another thread can observe it reliably | A flag announces that a single published value is ready | `Volatile.Read/Write`; synchronization also establishes visibility |
| Auto-reset event | Store one signal; release one waiter, then close again | Wake one worker for a notification | `AutoResetEvent` |
| Manual-reset event | Open a gate for every waiter until explicitly reset | Release all workers when startup completes | `ManualResetEventSlim`, `ManualResetEvent` |
| Countdown latch | Start with N outstanding completions; open when the count reaches zero | Coordinator waits for a batch of workers | `CountdownEvent` |
| Reusable barrier | All participants rendezvous at each phase before any continues | Multi-phase simulation | `Barrier` |
| Completion promise | An operation supplies a result once; consumers asynchronously wait for it | Adapt callbacks or announce startup without blocking | `TaskCompletionSource<T>` |
| Synchronized producer/consumer queue | Transfer ownership of items; bounded capacity can make producers wait | Background processing pipeline | `Channel<T>` for async work; `BlockingCollection<T>` for blocking work |

Mutex and monitor locks have thread ownership: the acquiring thread must release
them. Semaphores have no thread affinity, making `SemaphoreSlim.WaitAsync` suitable
for an async operation whose continuation may use another thread. Named mutexes
can synchronize processes; named semaphores and named events are supported on
Windows. The lightweight `Slim` types operate inside one process. See Microsoft's
[synchronization overview](https://learn.microsoft.com/en-us/dotnet/standard/threading/overview-of-synchronization-primitives).

The following sections explain each primitive through a small snippet. The source
classes show resource lifetime and complete runnable usage.

## 3. Protecting shared data

### Critical section: `lock`, `Lock`, and `Monitor`

```csharp
var gate = new Lock(); // Shared by every caller protecting this state.
int count = 0;
Parallel.For(0, 1_000, _ => { lock (gate) { count++; } });
// count == 1_000
```

With a statically typed `System.Threading.Lock`, the compiler uses its scoped
locking API. On older .NET versions, use `private readonly object _gate = new();`;
`lock(object)` uses `Monitor` and releases in `finally`. Both forms release even
if the body throws. See the [C# lock reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/lock).

Keep the section short. Use a private, dedicated gate, rather than `this`, a string,
or a publicly accessible object. Never `await` inside `lock`. Do not mix
`lock(Lock)` and object-monitor operations on that same `Lock` instance.
[`LockedCounter`](SharedStateExamples.cs) protects both reads and writes.

Explicit monitor acquisition is useful for a timeout:

```csharp
object gate = new();
bool taken = false;
try
{
    Monitor.TryEnter(gate, TimeSpan.FromSeconds(1), ref taken);
    if (!taken) throw new TimeoutException();
    // Synchronous critical section.
}
finally { if (taken) Monitor.Exit(gate); }
```

### Mutex: exclusive access between applications

```csharp
MutexExample.RunExclusive("InterviewToolkit_SharedResource", () =>
{
    // Synchronously update a resource protected by this name in every process.
});
```

The helper acquires a named `Mutex`, has a timeout, and releases only after ownership
was granted. The tour uses a unique name to avoid interfering with other applications.
The tour checks competing callers in one process; the shared-name mechanism is also
usable by separate processes. If an owner dies, acquisition can throw
`AbandonedMutexException` while granting ownership. Shared data might be inconsistent:
validate or repair it before continuing. The helper deliberately propagates that error
and releases ownership. Keep acquisition, work, and release on the same thread.

### Semaphore: bound access to N slots

```csharp
using var slots = new Semaphore(2, 2);
slots.WaitOne(); // Blocks the current thread until a permit is available.
try { /* Synchronous work with one resource slot. */ }
finally { slots.Release(); }
```

For asynchronous waiting:

```csharp
using var slots = new SemaphoreSlim(3, 3);
await slots.WaitAsync(token);
try { await ThreadingExamples.SimulatedIoAsync(2, token); }
finally { slots.Release(); }
```

Use `(1, 1)` to serialize asynchronous work. Always put the `try` **after** a successful
wait: cancellation before acquisition must not release a permit you never acquired.
Specifying the maximum count helps detect accidental extra releases. A semaphore is
a concurrency limit, not a requests-per-second rate limiter, and does not promise FIFO
fairness. [`AsyncConcurrencyLimiter`](CoordinationExamples.cs) wraps this pattern.
Its owner must await all users before disposing it.

### Condition variable: `Monitor.Wait` and `Pulse`

```csharp
object gate = new();
var queue = new Queue<int>();
var consumer = Task.Run(() =>
{
    lock (gate)
    {
        while (queue.Count == 0) Monitor.Wait(gate);
        return queue.Dequeue();
    }
});
lock (gate) { queue.Enqueue(42); Monitor.Pulse(gate); }
int item = await consumer;
```

`Wait` releases the lock so a producer can enter, then reacquires it before returning.
`Pulse` wakes one waiter; `PulseAll` wakes all. A pulse is not a stored permit and does
not immediately transfer the lock. Always check the predicate in a `while` loop:
another consumer could take the item before this waiter reacquires the lock.
Change the predicate and signal under the same lock. [`MonitorQueue<T>`](SharedStateExamples.cs)
adds completion to let a consumer exit when no more items will arrive. Its wait is
synchronous and has no token overload; prefer channels for cancellable async pipelines.

### Reader/writer lock

```csharp
using var gate = new ReaderWriterLockSlim();
gate.EnterReadLock();
try { /* Read shared data without mutating it. */ }
finally { gate.ExitReadLock(); }
gate.EnterWriteLock();
try { /* Update shared data exclusively. */ }
finally { gate.ExitWriteLock(); }
```

Read-heavy workloads may benefit; small operations may be faster with an ordinary
lock, so measure. Never `await` while holding it. To potentially write after reading,
use `EnterUpgradeableReadLock` and then a write lock; do not attempt to upgrade an
ordinary read lock directly. [`ReadMostlyCache`](SharedStateExamples.cs) returns immutable
strings so callers cannot mutate cache contents after the read lock ends.

### Spin lock and adaptive spin waiting

```csharp
var gate = new SpinLock(enableThreadOwnerTracking: true);
bool taken = false;
try { gate.Enter(ref taken); /* A tiny synchronous state update. */ }
finally { if (taken) gate.Exit(); }
```

Spinning spends CPU time waiting. Avoid I/O, blocking, async operations, and long
sections under a spin lock. `SpinLock` is a mutable struct: copying it or putting it
in a readonly field can operate on a different lock. [`SpinCounter`](SharedStateExamples.cs)
keeps one mutable field.

`SpinWait` is a waiting strategy, not mutual exclusion. It can spin briefly and then
yield. A publication example with one writer, no reset, and immutable published data:

```csharp
int ready = 0, result = 0;
var writer = Task.Run(() =>
{
    result = 42;
    Volatile.Write(ref ready, 1); // Publish result before announcing readiness.
});
if (!SpinWait.SpinUntil(() => Volatile.Read(ref ready) == 1,
                       TimeSpan.FromSeconds(5))) throw new TimeoutException();
Console.WriteLine(result);
await writer;
```

For ordinary application code, completion tasks or events express the intent more
clearly and avoid a polling protocol.

### Atomic operations and memory visibility

```csharp
int count = 0;
Interlocked.Increment(ref count);
int claimed = 0;
bool won = Interlocked.CompareExchange(ref claimed, 1, 0) == 0;
int snapshot = Volatile.Read(ref count);
```

`CompareExchange` returns the previous value; only one contender changes a shared
claim from 0 to 1. `Volatile` supports visibility and ordering but does **not** make
`count++` or a multi-field transaction atomic. Atomics simplify counters;
multi-step invariants usually need a lock. [`AtomicCounter`](SharedStateExamples.cs)
combines atomic increments with an ordered read.

## 4. Coordinating workers

### Auto-reset event: release one waiter

```csharp
using var ready = new AutoResetEvent(false);
var worker = Task.Run(() => { ready.WaitOne(); return 42; });
ready.Set();
int value = await worker;
```

A signal with no waiter remains available for one future wait. Multiple `Set` calls
can coalesce into one signal; it is not a reliable count of queued jobs. Use a queue
plus notification, a semaphore, or a channel when every item matters.

### Manual-reset event: open a gate for all

```csharp
using var ready = new ManualResetEventSlim(false);
var workers = Enumerable.Range(0, 3)
    .Select(_ => Task.Run(() => ready.Wait())).ToArray();
ready.Set();
await Task.WhenAll(workers);
ready.Reset(); // Closes the gate for future waits, after this phase is finished.
```

`ManualResetEvent` provides the same reset semantics through an OS wait handle;
`ManualResetEventSlim` is a lightweight in-process alternative. `EventWaitHandle`
is the underlying configurable OS-event type:

```csharp
using var ready = new EventWaitHandle(false, EventResetMode.ManualReset);
ready.Set();
ready.WaitOne();
ready.Reset();
```

Waits block threads. For an async one-time startup gate use a completion source below.

### Countdown latch: wait for all completions

```csharp
using var done = new CountdownEvent(3);
var workers = Enumerable.Range(0, 3).Select(_ => Task.Run(() =>
{
    try { /* Work. */ }
    finally { done.Signal(); }
})).ToArray();
done.Wait();
await Task.WhenAll(workers); // Observe exceptions too.
```

The count reaches zero after three signals. The latch alone does not propagate worker
exceptions. With tasks, `await Task.WhenAll` often replaces the latch entirely.

### Barrier: rendezvous between reusable phases

```csharp
using var barrier = new Barrier(2);
var workers = Enumerable.Range(0, 2).Select(_ => new Thread(() =>
{
    for (int phase = 0; phase < 2; phase++)
    {
        // Perform this participant's phase work.
        barrier.SignalAndWait();
        // Every participant has finished this phase.
    }
})).ToArray();
foreach (var worker in workers) worker.Start();
foreach (var worker in workers) worker.Join();
```

Missing participants cause indefinite waiting in this minimal snippet. Real code must
handle participant failure, cancellation, or timeouts; remove departing participants
when appropriate. [`BarrierPhases`](CoordinationExamples.cs) uses timeouts and collects
thread exceptions. Dedicated threads also avoid pool starvation from participants
blocking while other participants are still queued.

### Completion source: asynchronous one-time notification

```csharp
var ready = new TaskCompletionSource<int>(
    TaskCreationOptions.RunContinuationsAsynchronously);
Task<int> waiter = ready.Task;
ready.TrySetResult(42); // A callback or producer would normally do this.
int value = await waiter;
```

`TrySetException` and `TrySetCanceled` can complete it in other states. Completion
is permanent. The continuations option prevents arbitrary awaiter code from running
inline inside the producer's completion call. See `ThreadPoolWorkAsync` and the tour's
startup handshake for complete examples.

### Producer/consumer collections

```csharp
using System.Threading.Channels;
var channel = Channel.CreateBounded<int>(2);
await channel.Writer.WriteAsync(42, token);
channel.Writer.Complete();
await foreach (int item in channel.Reader.ReadAllAsync(token))
    Console.WriteLine(item);
```

Bounded channels provide **backpressure**: in wait mode, a producer awaits capacity
when consumers fall behind. Start consumers before filling a bounded channel beyond
capacity. [`ChannelPipelineAsync`](CoordinationExamples.cs) starts both sides, signals
completion even if production fails, passes cancellation through, and awaits both.

The blocking equivalent is useful for dedicated synchronous workers:

```csharp
using System.Collections.Concurrent;
using var queue = new BlockingCollection<int>(boundedCapacity: 2);
var consumer = Task.Run(() =>
{
    foreach (int item in queue.GetConsumingEnumerable(token)) Console.WriteLine(item);
});
queue.Add(42, token);
queue.CompleteAdding();
await consumer;
```

`ConcurrentDictionary`, `ConcurrentQueue`, and similar collections make their own
operations thread-safe. They do not protect mutable stored objects or arbitrary
check-then-act sequences. `ConcurrentDictionary.GetOrAdd` factories can run more
than once, so avoid relying on exactly-once side effects. `ForEachAsyncSquares`
uses a concurrent dictionary to gather independent results.

## 5. Threading from the basics to tasks

### `Thread`: direct control over an execution thread

```csharp
var worker = new Thread(() => Console.WriteLine("Worker"));
worker.Start();
worker.Join(); // Wait synchronously for termination.
```

Use explicit threads for requirements such as a dedicated long-running synchronous
loop, not for every request. Foreground threads can keep the process alive; background
threads do not guarantee completion before exit. `Thread.Sleep` blocks the current
thread and should not be used to establish synchronization. Unhandled exceptions
on raw threads can terminate the process; capture them in the worker and report to
its owner. `DedicatedThread` demonstrates a simple result after `Join`.

### `ThreadPool`: reuse managed workers

```csharp
int answer = await ThreadingExamples.ThreadPoolWorkAsync();
```

The class uses `ThreadPool.QueueUserWorkItem` and a completion source to bridge the
callback to an awaitable result. Pool threads are background threads. Blocking many
pool workers can delay other work; do not change global pool settings as a first fix.
For ordinary work, tasks provide a simpler result/exception abstraction.

### `Task.Run`: schedule CPU work

```csharp
long sum = await ThreadingExamples.CpuWorkAsync(100_000, token);
```

The method schedules a CPU loop on the pool and checks the token during computation.
This can keep a UI responsive, but offloading server CPU work does not reduce total
CPU cost. Do not wrap naturally asynchronous I/O in `Task.Run` just to await it.
See Microsoft's [task-based programming guide](https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/task-based-asynchronous-programming).

### `async` / `await`: suspend instead of blocking

```csharp
int result = await ThreadingExamples.SimulatedIoAsync(3, token);
// The example uses Task.Delay; real code might await an HTTP or file API.
```

An async method executes synchronously until an incomplete await. A continuation
may resume on another thread, or a captured synchronization context such as a UI
thread. Do not assume thread identity is stable. Prefer returning `Task`/`Task<T>`;
`async void` is mainly for required event handlers and cannot be awaited by callers.
Avoid `.Result` and `.Wait()` on async work: they block and can deadlock a UI context.

### Multiple tasks: `WhenAll` and `WhenAny`

```csharp
Task<int>[] work = [ThreadingExamples.SimulatedIoAsync(2, token),
                    ThreadingExamples.SimulatedIoAsync(3, token)];
int[] results = await Task.WhenAll(work); // Results follow input-task order.
```

The operations start when called, not when `WhenAll` is awaited. `WhenAll` waits for
all tasks; a fault takes precedence over cancellation. An awaited fault usually
throws one exception; the combined task's `Exception` contains the collected faults.
It does not cancel remaining work when one fails. Creating tasks for millions of
items can consume too much memory; use a bounded pipeline or `ForEachAsync`.

```csharp
Task<int> first = await Task.WhenAny(work);
int firstResult = await first; // Observe the winning task's result or exception.
await Task.WhenAll(work);      // Observe and finish the remaining tasks too.
```

`WhenAny` completes successfully with the winning task even when that task failed
or canceled. It does not stop the others. `WhenAnyAsync` drains both sample operations.

## 6. Cooperative cancellation

`CancellationTokenSource` owns the ability to request cancellation. Its token is a
lightweight value passed to work. `Cancel()` announces a request; it does not kill a
thread. Code must poll or call cancellable APIs. `ThrowIfCancellationRequested`
throws `OperationCanceledException` carrying the token. See Microsoft's
[cancellation guide](https://learn.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads).

```csharp
using var stop = new CancellationTokenSource();
stop.CancelAfter(TimeSpan.FromSeconds(1));
try
{
    await ThreadingExamples.CancellableWorkerAsync(() =>
        Console.WriteLine("Started"), stop.Token);
}
catch (OperationCanceledException) when (stop.IsCancellationRequested)
{
    Console.WriteLine("Cancellation acknowledged");
}
```

The worker checks the token and passes it to `Task.Delay`. Supply tokens all the way
down through file/HTTP calls, semaphore waits, channel operations, and loop options.
The token argument to `Task.Run` can prevent work from starting; it does not abort
an already-running delegate. That delegate must check it itself.

Combine a caller's token with a deadline:

```csharp
using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
deadline.CancelAfter(TimeSpan.FromSeconds(2));
int result = await ThreadingExamples.SimulatedIoAsync(4, deadline.Token);
```

Use `finally` or `using` for cleanup, and finish all workers before disposing resources
they use. Disposing a source does not request cancellation. Cancellation is not rollback:
an already-written file or sent request may have taken effect. `Task.WaitAsync(token)`
cancels the caller's wait; it does not cancel the underlying task. To stop underlying
work, pass a token into that operation too.

## 7. Parallel loops and queries

### `Parallel.For`: independent indexed CPU work

```csharp
int[] squares = ParallelExamples.ForSquares([1, 2, 3, 4], token);
```

The class writes each output index once, so the array requires no lock. Iterations
can execute in any order. `Parallel.For` blocks until completion. Use it when each
iteration has enough CPU work to offset scheduling overhead; these small examples
demonstrate correctness, not a performance win.

### `Parallel.ForEach`: CPU work over a sequence

```csharp
long sum = ParallelExamples.ForEachSum([1, 2, 3, 4], token);
```

The class uses worker-local sums and an atomic merge, avoiding a shared update on
every element. Never append concurrently to a normal `List<T>`. Choose separate
output slots, a concurrent collection, or local accumulation.

`ParallelOptions` supplies cancellation and `MaxDegreeOfParallelism`. A cap limits
simultaneous operations, not necessarily a fixed set of dedicated threads. Check
the token inside long iteration bodies so cancellation does not wait for one huge
iteration. Parallel loops can throw `AggregateException` for worker faults. Cancellation
using the options' token is reported as `OperationCanceledException`.

`ParallelLoopState.Stop()` requests that iterations stop without an ordering
requirement. `Break()` asks the loop to finish iterations below the lowest break index
and avoid starting higher ones where possible. Already-running iterations may continue;
neither is a thread abort.

### `Parallel.Invoke`: a few independent synchronous actions

```csharp
Parallel.Invoke(() => Console.WriteLine("First operation"),
                () => Console.WriteLine("Second operation"));
```

The call returns after both actions finish. Execution order is unspecified. Use it
for independent CPU-heavy actions, not asynchronous delegates.

### PLINQ: parallel query processing

```csharp
int[] squares = ParallelExamples.PlinqSquares([1, 2, 3, 4], token);
```

`AsParallel` enables parallel execution; `AsOrdered` preserves source ordering at
an additional cost. Avoid side effects and shared mutable accumulators in query
delegates. Enumeration triggers execution; these examples materialize with `ToArray`.

### `Parallel.ForEachAsync`: bounded asynchronous work

```csharp
await Parallel.ForEachAsync(Enumerable.Range(1, 20), new ParallelOptions
{
    MaxDegreeOfParallelism = 3,
    CancellationToken = token
}, async (id, iterationToken) =>
{
    int result = await ThreadingExamples.SimulatedIoAsync(id, iterationToken);
    Console.WriteLine(result);
});
```

Use the supplied iteration token inside the delegate. At most three iteration bodies
run concurrently in this example; console output is not ordered. The returned task
represents the entire loop. Tune I/O concurrency to the service or resource capacity;
CPU-core count is not necessarily the right limit for I/O. The default overload uses
processor count as its concurrency bound. See the
[ForEachAsync API](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.parallel.foreachasync?view=net-10.0).

**Do not pass an async lambda to `Parallel.ForEach`**: its synchronous action delegate
can turn that lambda into `async void`, letting the loop return before work completes
and losing normal task exception handling. Use `ForEachAsync` or compose tasks instead.

## 8. Choosing an approach and avoiding common failures

| Need | Starting point |
| --- | --- |
| Protect a short multi-step shared invariant | `lock` |
| Increment a shared counter | `Interlocked` |
| Serialize async work or cap concurrent I/O | `SemaphoreSlim.WaitAsync` |
| Await a few existing asynchronous operations | `Task.WhenAll` |
| Process many independent CPU-heavy items | `Parallel.For` / `Parallel.ForEach` / PLINQ |
| Process many async items with a bound | `Parallel.ForEachAsync` |
| Stream jobs between producers and consumers | Bounded `Channel<T>` |
| Announce one asynchronous completion | `TaskCompletionSource<T>` |
| Coordinate ownership between processes | Named `Mutex` |

- **Deadlock:** A holds lock 1 and wants 2; B holds 2 and wants 1. Acquire multiple
  locks in a consistent global order. Avoid callbacks or blocking I/O while holding locks.
- **Starvation:** A participant cannot get a resource or a pool thread. Do not assume
  strict fairness, and avoid blocking all pool workers while awaiting queued peers.
- **Livelock:** Participants keep reacting to each other without completing useful work.
  Simplify the protocol or use bounded backoff rather than endless retries.
- **Contention:** Too many workers compete for one gate. Reduce shared state, accumulate
  locally, and keep sections short before adding more workers.
- **Hidden thread affinity:** Monitor, mutex, and reader/writer ownership must remain
  on the same thread. Use async-aware primitives across awaits.
- **False confidence from one run:** Races are schedule-dependent. Passing the tour
  checks example behavior, not every possible interleaving or a performance guarantee.

## 9. Source map and executable checks

| File | Examples |
| --- | --- |
| [SharedStateExamples.cs](SharedStateExamples.cs) | Lock, atomic and spin counters; atomic claim; Volatile/SpinWait publication; monitor queue; reader/writer cache; named mutex |
| [CoordinationExamples.cs](CoordinationExamples.cs) | Async semaphore limiter; OS semaphore; auto/manual events; countdown; reusable barrier; bounded channel; BlockingCollection pipeline |
| [ThreadingExamples.cs](ThreadingExamples.cs) | Dedicated thread, raw pool callback, CPU task, async wait, WhenAll/WhenAny, cancellable worker |
| [ParallelExamples.cs](ParallelExamples.cs) | For, ForEach reduction, Invoke, PLINQ, ForEachAsync with concurrent results |
| [ConcurrencyTour.cs](ConcurrencyTour.cs) | Runs these examples and throws if a result violates its expected behavior |

The tour checks lost-update prevention with multiple workers, queue completion,
semaphore capacity and release after failure, signals and phase counts, computed
results, and cancellation. It deliberately starts a worker before requesting
cancellation, and cancels a semaphore caller while both permits are held. No
assertion depends on which worker happens to finish first.
