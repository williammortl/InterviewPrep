using System.Diagnostics.CodeAnalysis;

namespace InterviewToolkit.DataStructures;

/*
 * Priority queue
 * --------------
 * Items come out in priority order, not insertion order. This class does not
 * reimplement the tree arithmetic. It stores an entry (element + priority +
 * sequence number) in a Heap, and the heap keeps the winning entry at the root.
 *
 * The heap is always a min-heap over those entries:
 *   - smaller priority, as defined by the comparer, is dequeued first
 *   - when two priorities compare equal, the smaller sequence number wins
 *
 * The sequence number is the enqueue count, so equal priorities leave in FIFO
 * order (the earlier enqueue first). That is not true of a bare heap, which
 * treats equal keys as ties and may surface either one.
 *
 * To dequeue the largest priority first, pass a comparer that reverses the
 * natural order, for example Comparer<int>.Create((a, b) => b.CompareTo(a)).
 * The sequence tie-break still prefers the earlier item, because the reversal
 * lives in the priority comparer and the sequence comparison is unchanged.
 */

/// <summary>
/// A priority queue of <typeparamref name="TElement"/> ordered by <typeparamref name="TPriority"/>.
/// Backed by <see cref="Heap{T}"/>.
/// </summary>
/// <typeparam name="TElement">The value stored with each enqueue. This is what dequeue returns.</typeparam>
/// <typeparam name="TPriority">The priority used to order entries. Compared with the supplied comparer.</typeparam>
public sealed class PriorityQueue<TElement, TPriority>
{
    /// <summary>
    /// One queued item. A struct so enqueue does not allocate an object per item;
    /// the heap moves entries by copying them during swaps.
    /// </summary>
    private readonly struct Entry
    {
        public Entry(TElement element, TPriority priority, long sequence)
        {
            Element = element;
            Priority = priority;
            Sequence = sequence;
        }

        public TElement Element { get; }
        public TPriority Priority { get; }
        public long Sequence { get; }
    }

    /// <summary>
    /// Orders entries for the min-heap. Priority decides first. Sequence decides
    /// only when the priorities compare equal, and a smaller sequence (earlier
    /// enqueue) compares as less, so it sits closer to the root.
    /// </summary>
    private sealed class EntryComparer : IComparer<Entry>
    {
        private readonly IComparer<TPriority> _priorityComparer;

        public EntryComparer(IComparer<TPriority> priorityComparer) =>
            _priorityComparer = priorityComparer;

        public int Compare(Entry x, Entry y)
        {
            int byPriority = _priorityComparer.Compare(x.Priority, y.Priority);
            if (byPriority != 0)
                return byPriority;

            return x.Sequence.CompareTo(y.Sequence);
        }
    }

    private readonly Heap<Entry> _heap;
    private long _nextSequence;

    /// <summary>
    /// Creates an empty queue. <paramref name="priorityComparer"/> decides what
    /// "smaller priority" means. The default comparer uses the type's own ordering.
    /// </summary>
    public PriorityQueue(IComparer<TPriority>? priorityComparer = null)
    {
        var comparer = priorityComparer ?? Comparer<TPriority>.Default;
        _heap = new Heap<Entry>(HeapOrder.Min, new EntryComparer(comparer));
    }

    /// <summary>How many items are waiting. O(1).</summary>
    public int Count => _heap.Count;

    /// <summary><see langword="true"/> when nothing is queued.</summary>
    public bool IsEmpty => _heap.IsEmpty;

    /// <summary>
    /// Adds <paramref name="element"/> with the given <paramref name="priority"/>.
    /// O(log n) because it is a heap insert. The element is not compared; only the priority is.
    /// </summary>
    public void Enqueue(TElement element, TPriority priority)
    {
        if (_nextSequence == long.MaxValue)
        {
            throw new InvalidOperationException(
                "The queue cannot accept another item without losing FIFO ordering of equal priorities.");
        }

        _heap.Insert(new Entry(element, priority, _nextSequence));
        _nextSequence++;
    }

    /// <summary>
    /// Removes and returns the element with the best (smallest) priority.
    /// Throws <see cref="InvalidOperationException"/> when the queue is empty. O(log n).
    /// </summary>
    public TElement Dequeue()
    {
        if (!TryDequeue(out var element))
            throw new InvalidOperationException("The priority queue is empty.");

        return element;
    }

    /// <summary>
    /// Removes the best element. Returns <see langword="false"/> when the queue is empty.
    /// </summary>
    public bool TryDequeue([MaybeNullWhen(false)] out TElement element)
    {
        if (!_heap.TryExtract(out var entry))
        {
            element = default;
            return false;
        }

        element = entry.Element;
        return true;
    }

    /// <summary>
    /// Returns the best element without removing it.
    /// Throws <see cref="InvalidOperationException"/> when the queue is empty. O(1).
    /// </summary>
    public TElement Peek()
    {
        if (!TryPeek(out var element))
            throw new InvalidOperationException("The priority queue is empty.");

        return element;
    }

    /// <summary>The priority of the item <see cref="Peek"/> would return. O(1).</summary>
    public TPriority PeekPriority()
    {
        if (_heap.IsEmpty)
            throw new InvalidOperationException("The priority queue is empty.");

        return _heap.Peek().Priority;
    }

    /// <summary>
    /// Returns the best element without removing it.
    /// <see langword="false"/> when the queue is empty.
    /// </summary>
    public bool TryPeek([MaybeNullWhen(false)] out TElement element)
    {
        if (!_heap.TryPeek(out var entry))
        {
            element = default;
            return false;
        }

        element = entry.Element;
        return true;
    }

    /// <summary>Drops every queued item. O(n).</summary>
    public void Clear() => _heap.Clear();
}
