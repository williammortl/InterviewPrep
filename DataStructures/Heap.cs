namespace InterviewToolkit.DataStructures;

/*
 * Binary heap
 * -----------
 * A complete binary tree packed into a list. "Complete" means every level is full
 * except possibly the last, which is filled from the left. There are no node objects:
 * the parent/child relationship is arithmetic on the index.
 *
 *              1
 *            /   \
 *           3     5
 *          / \
 *         8   4
 *
 *   index:  0  1  2  3  4
 *   value: [1, 3, 5, 8, 4]
 *
 *   parent of i       = (i - 1) / 2
 *   left child of i   = 2 * i + 1
 *   right child of i  = 2 * i + 2
 *
 * Min-heap property: every parent is less than or equal to both children,
 * so the smallest item sits at index 0. A max-heap flips the comparison,
 * so the largest item sits at index 0.
 *
 * Insert and extract only walk one path from root to leaf, which is O(log n)
 * in a complete tree. Building a heap from n items by sifting down from the
 * middle is O(n), which is better than inserting them one by one.
 */

/// <summary>Whether the root holds the smallest item or the largest item.</summary>
public enum HeapOrder
{
    /// <summary>The smallest item, according to the comparer, is at the root.</summary>
    Min,

    /// <summary>The largest item, according to the comparer, is at the root.</summary>
    Max
}

/// <summary>
/// A binary heap of <typeparamref name="T"/>. The root is always the next item
/// that <see cref="Extract"/> would return.
/// </summary>
/// <typeparam name="T">
/// The item type. It does not need to implement <see cref="IComparable{T}"/> when
/// a comparer is passed in. The default comparer requires that <typeparamref name="T"/>
/// be comparable, and throws when it is not.
/// </typeparam>
public sealed class Heap<T>
{
    private readonly List<T> _items;
    private readonly IComparer<T> _comparer;

    /// <summary>Creates an empty heap. The default order is a min-heap.</summary>
    public Heap(HeapOrder order = HeapOrder.Min, IComparer<T>? comparer = null)
    {
        Order = order;
        _comparer = comparer ?? Comparer<T>.Default;
        _items = new List<T>();
    }

    /// <summary>
    /// Builds a heap from <paramref name="items"/> in O(n) using Floyd's algorithm:
    /// the leaves are already valid heaps of one node, so sifting starts at the
    /// last parent and walks up to the root.
    /// </summary>
    public Heap(IEnumerable<T> items, HeapOrder order = HeapOrder.Min, IComparer<T>? comparer = null)
        : this(order, comparer)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items.AddRange(items);

        // The last parent is the parent of the last index. Leaves have no children,
        // so they already satisfy the heap property and can be skipped.
        for (int index = (_items.Count / 2) - 1; index >= 0; index--)
            SiftDown(index);
    }

    /// <summary>Min-heap or max-heap. Fixed for the lifetime of this instance.</summary>
    public HeapOrder Order { get; }

    /// <summary>Number of items currently stored. O(1).</summary>
    public int Count => _items.Count;

    /// <summary><see langword="true"/> when <see cref="Count"/> is zero.</summary>
    public bool IsEmpty => _items.Count == 0;

    /// <summary>
    /// Adds <paramref name="item"/> at the next open leaf (the end of the list),
    /// then swaps it upward until the heap property holds. O(log n).
    /// </summary>
    public void Insert(T item)
    {
        _items.Add(item);
        SiftUp(_items.Count - 1);
    }

    /// <summary>
    /// Returns the root without removing it. O(1).
    /// Throws <see cref="InvalidOperationException"/> when the heap is empty.
    /// </summary>
    public T Peek()
    {
        if (_items.Count == 0)
            throw new InvalidOperationException("The heap is empty.");

        return _items[0];
    }

    /// <summary>
    /// Returns the root without removing it.
    /// <see langword="false"/> when the heap is empty.
    /// </summary>
    public bool TryPeek(out T item)
    {
        if (_items.Count == 0)
        {
            item = default!;
            return false;
        }

        item = _items[0];
        return true;
    }

    /// <summary>
    /// Removes and returns the root, then repairs the heap. O(log n).
    /// Throws <see cref="InvalidOperationException"/> when the heap is empty.
    /// </summary>
    /// <remarks>
    /// The last leaf moves into the hole at index 0 (that keeps the tree complete),
    /// then <see cref="SiftDown"/> walks it down until both children lose to it.
    /// </remarks>
    public T Extract()
    {
        if (!TryExtract(out var item))
            throw new InvalidOperationException("The heap is empty.");

        return item;
    }

    /// <summary>
    /// Removes and returns the root. <see langword="false"/> when the heap is empty.
    /// </summary>
    public bool TryExtract(out T item)
    {
        if (_items.Count == 0)
        {
            item = default!;
            return false;
        }

        item = _items[0];
        int lastIndex = _items.Count - 1;
        _items[0] = _items[lastIndex];
        _items.RemoveAt(lastIndex);

        if (_items.Count > 0)
            SiftDown(0);

        return true;
    }

    /// <summary>
    /// Replaces the first item equal to <paramref name="current"/> with <paramref name="updated"/>
    /// and moves that slot up or down until the heap property holds.
    /// </summary>
    /// <remarks>
    /// A heap does not keep an index by value, so the search is a linear scan: O(n).
    /// The repair afterward is O(log n). When several items compare equal, the one
    /// closest to index 0 is the one that changes. Returns <see langword="false"/>
    /// when <paramref name="current"/> is not found. Equality uses
    /// <see cref="EqualityComparer{T}.Default"/>, which can differ from the sort comparer.
    /// </remarks>
    public bool Edit(T current, T updated)
    {
        var equality = EqualityComparer<T>.Default;
        for (int index = 0; index < _items.Count; index++)
        {
            if (!equality.Equals(_items[index], current))
                continue;

            _items[index] = updated;

            // SiftUp returns the slot the item landed in. If it moved up, SiftDown
            // on that new slot stops immediately. If it did not move up, SiftDown
            // is what repairs a value that got larger (in a min-heap) or smaller
            // (in a max-heap). Using the original index after SiftUp would repair
            // whatever item fell into the hole, not the item we just wrote.
            int landed = SiftUp(index);
            SiftDown(landed);
            return true;
        }

        return false;
    }

    /// <summary>Removes every item. O(n) because the backing list is cleared.</summary>
    public void Clear() => _items.Clear();

    /// <summary>
    /// <see langword="true"/> when the item at <paramref name="index"/> belongs
    /// closer to the root than the item at <paramref name="other"/>.
    /// </summary>
    private bool Outranks(int index, int other)
    {
        int comparison = _comparer.Compare(_items[index], _items[other]);
        return Order == HeapOrder.Min ? comparison < 0 : comparison > 0;
    }

    /// <summary>
    /// Swaps the item upward while it outranks its parent.
    /// Returns the index where the item stopped.
    /// </summary>
    private int SiftUp(int index)
    {
        while (index > 0)
        {
            int parent = (index - 1) / 2;
            if (!Outranks(index, parent))
                break;

            Swap(index, parent);
            index = parent;
        }

        return index;
    }

    /// <summary>
    /// Swaps the item downward while a child outranks it. The child that wins
    /// is the one that itself outranks the other child, so the heap property
    /// holds on both sides after the swap.
    /// </summary>
    private void SiftDown(int index)
    {
        int count = _items.Count;
        while (true)
        {
            int left = (index * 2) + 1;
            int right = left + 1;
            int best = index;

            if (left < count && Outranks(left, best))
                best = left;

            if (right < count && Outranks(right, best))
                best = right;

            // Both children lose to the current item (or there are no children).
            if (best == index)
                break;

            Swap(index, best);
            index = best;
        }
    }

    private void Swap(int left, int right)
    {
        (_items[left], _items[right]) = (_items[right], _items[left]);
    }
}
