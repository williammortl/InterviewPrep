using System.Collections;

namespace InterviewToolkit.DataStructures;

/*
 * Doubly linked list
 * ------------------
 * Each element lives in a node with two references: Next and Previous.
 * The list remembers the first node (head) and the last node (tail).
 *
 *   head                                         tail
 *    |                                            |
 *   [A] <--> [B] <--> [C] <--> [D]
 *
 * Previous is what a singly linked list does not have. With it, we can:
 *   - walk backward from the tail (the indexer uses this when the index is near the end)
 *   - unlink a node in O(1) once we are holding it
 *   - reverse the list by swapping the two links on every node
 *
 * There is no array underneath. list[i] walks node to node, so random access is O(n),
 * not O(1). Length is stored separately so asking for the size does not walk the list.
 */

/// <summary>
/// A doubly linked list of <typeparamref name="T"/>. Supports indexed read and write
/// through <c>list[index]</c>, in-place reverse, and edit and delete.
/// </summary>
/// <typeparam name="T">The value stored in each node.</typeparam>
public sealed class DoublyLinkedList<T> : IEnumerable<T>
{
    /// <summary>
    /// One link in the chain. Private because callers address elements by index;
    /// handing out nodes would let outside code rewire Next and Previous.
    /// </summary>
    private sealed class Node
    {
        public Node(T value) => Value = value;

        public T Value;
        public Node? Previous;
        public Node? Next;
    }

    private Node? _head;
    private Node? _tail;
    private int _length;

    // Bumped on insert, delete, reverse, and clear. The enumerator captures the
    // value and throws if the chain changes under it. Editing a value in place
    // does not change the links, so it does not bump the version.
    private int _version;

    /// <summary>
    /// Number of nodes. Cached, so reading it is O(1) — the list is not walked.
    /// </summary>
    public int Length => _length;

    /// <summary><see langword="true"/> when <see cref="Length"/> is zero.</summary>
    public bool IsEmpty => _length == 0;

    /// <summary>
    /// Gets or sets the value at <paramref name="index"/>.
    /// </summary>
    /// <remarks>
    /// C# binds <c>list[index]</c> to this indexer. There is no <c>operator []</c>
    /// to override the way C++ has one; the indexer is the language feature that
    /// implements that syntax. The setter is an in-place edit: the node stays,
    /// only <see cref="Node.Value"/> changes.
    /// <para>
    /// Time is O(min(index, Length - index)) because the walk starts at the head
    /// or the tail, whichever is closer.
    /// </para>
    /// </remarks>
    public T this[int index]
    {
        get => NodeAt(index).Value;
        set => NodeAt(index).Value = value;
    }

    /// <summary>Inserts <paramref name="value"/> before the current head. O(1).</summary>
    public void AddFirst(T value)
    {
        var node = new Node(value);
        if (_head is null)
        {
            // Empty list: the new node is both ends.
            _head = node;
            _tail = node;
        }
        else
        {
            node.Next = _head;
            _head.Previous = node;
            _head = node;
        }

        _length++;
        _version++;
    }

    /// <summary>Appends <paramref name="value"/> after the current tail. O(1).</summary>
    public void AddLast(T value)
    {
        var node = new Node(value);
        if (_tail is null)
        {
            _head = node;
            _tail = node;
        }
        else
        {
            node.Previous = _tail;
            _tail.Next = node;
            _tail = node;
        }

        _length++;
        _version++;
    }

    /// <summary>
    /// Inserts <paramref name="value"/> so that it lands at <paramref name="index"/>.
    /// <paramref name="index"/> may be <see cref="Length"/>, which appends.
    /// Finding the spot is O(n); splicing the node in is O(1).
    /// </summary>
    public void Insert(int index, T value)
    {
        if ((uint)index > (uint)_length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"Insertion index must be between 0 and {_length}.");
        }

        if (index == _length)
        {
            AddLast(value);
            return;
        }

        if (index == 0)
        {
            AddFirst(value);
            return;
        }

        // Splice the new node in front of the node that currently occupies this index.
        var next = NodeAt(index);
        var node = new Node(value)
        {
            Previous = next.Previous,
            Next = next
        };

        next.Previous!.Next = node;
        next.Previous = node;
        _length++;
        _version++;
    }

    /// <summary>
    /// Replaces the value at <paramref name="index"/>. Equivalent to <c>list[index] = newValue</c>.
    /// The links do not move. O(n) to reach the node, then O(1) to write the value.
    /// </summary>
    public void Edit(int index, T newValue) => this[index] = newValue;

    /// <summary>
    /// Removes the node at <paramref name="index"/> and reconnects its neighbors.
    /// Throws <see cref="ArgumentOutOfRangeException"/> when the index is outside the list.
    /// O(n) to find the node, O(1) to unlink it.
    /// </summary>
    /// <remarks>
    /// This is <c>DeleteAt</c> rather than an overload of <c>Delete(T)</c>. When
    /// <typeparamref name="T"/> is <see cref="int"/>, a single <c>Delete(int)</c>
    /// could not mean both "remove this index" and "remove this value".
    /// </remarks>
    public void DeleteAt(int index) => Unlink(NodeAt(index));

    /// <summary>
    /// Removes the first node whose value equals <paramref name="value"/>.
    /// Returns <see langword="false"/> when nothing matches. Equality uses
    /// <see cref="EqualityComparer{T}.Default"/>. O(n).
    /// </summary>
    public bool Delete(T value)
    {
        var equality = EqualityComparer<T>.Default;
        for (var current = _head; current != null; current = current.Next)
        {
            if (!equality.Equals(current.Value, value))
                continue;

            Unlink(current);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Reverses the list in place. O(n) time, O(1) extra memory.
    /// </summary>
    /// <remarks>
    /// Each node swaps its Next and Previous pointers. After every node has been
    /// flipped, the old tail is the head and the old head is the tail.
    /// <code>
    /// Before:  head [A] <--> [B] <--> [C] tail
    /// After:   head [C] <--> [B] <--> [A] tail
    /// </code>
    /// A list of zero or one node is already its own reverse.
    /// </remarks>
    public void Reverse()
    {
        if (_length < 2)
            return;

        var current = _head;
        while (current != null)
        {
            // Remember the old Next first. After the swap, Next points backward,
            // so it is no longer a way to advance through the original order.
            var next = current.Next;
            current.Next = current.Previous;
            current.Previous = next;
            current = next;
        }

        (_head, _tail) = (_tail, _head);
        _version++;
    }

    /// <summary><see langword="true"/> when some node equals <paramref name="value"/>. O(n).</summary>
    public bool Contains(T value)
    {
        var equality = EqualityComparer<T>.Default;
        for (var current = _head; current != null; current = current.Next)
        {
            if (equality.Equals(current.Value, value))
                return true;
        }

        return false;
    }

    /// <summary>Drops every node. The chain becomes unreachable and can be collected. O(1).</summary>
    public void Clear()
    {
        _head = null;
        _tail = null;
        _length = 0;
        _version++;
    }

    /// <summary>Walks from head to tail. Throws if the list is mutated during the walk.</summary>
    public IEnumerator<T> GetEnumerator()
    {
        int version = _version;
        for (var current = _head; current != null; current = current.Next)
        {
            if (version != _version)
                throw new InvalidOperationException("The list was modified during enumeration.");

            yield return current.Value;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Finds the node at <paramref name="index"/>, starting from the nearer end.
    /// The unsigned comparison treats a negative index as out of range: casting a
    /// negative <see cref="int"/> to <see cref="uint"/> produces a value larger than any list.
    /// </summary>
    private Node NodeAt(int index)
    {
        if ((uint)index >= (uint)_length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"Index is outside the list (length {_length}).");
        }

        if (index <= (_length - 1) / 2)
        {
            var current = _head!;
            for (int step = 0; step < index; step++)
                current = current.Next!;
            return current;
        }

        var backward = _tail!;
        for (int step = _length - 1; step > index; step--)
            backward = backward.Previous!;
        return backward;
    }

    /// <summary>
    /// Cuts <paramref name="node"/> out of the chain and connects its neighbors to each other.
    /// Head and tail are updated when the removed node was an end. O(1).
    /// </summary>
    private void Unlink(Node node)
    {
        if (node.Previous is null)
            _head = node.Next;
        else
            node.Previous.Next = node.Next;

        if (node.Next is null)
            _tail = node.Previous;
        else
            node.Next.Previous = node.Previous;

        // Drop the removed node's links so it does not keep the rest of the list alive.
        node.Next = null;
        node.Previous = null;
        _length--;
        _version++;
    }
}
