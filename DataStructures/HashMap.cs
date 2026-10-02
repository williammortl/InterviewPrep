using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace InterviewToolkit.DataStructures;

/*
 * Hash map (separate chaining)
 * ----------------------------
 * A key is converted to a bucket index. Each bucket is a singly linked list of
 * the entries whose hash landed there. Two different keys can share a bucket;
 * that is a collision, and the chain is how this map resolves it.
 *
 *   bucket:   0       1        2       3
 *             |       |        |       |
 *            [k]     [k]     null    [k] --> [k] --> [k]
 *
 * Lookup walks only that one chain, comparing hash codes first and then keys.
 * When the hash function spreads keys out and the table grows before chains get
 * long, add / get / edit / delete are O(1) on average. If every key hits the
 * same bucket, the chain is n long and those operations become O(n).
 *
 * The bucket array length is always a power of two so the index is
 * (mixedHash & (length - 1)) instead of a remainder. A negative hash code is
 * safe under a bitmask; it is not safe under the % operator, which keeps the
 * sign in C#. The mix step folds the high bits into the low bits, because a
 * plain mask would otherwise ignore everything above the bucket count.
 *
 * The table grows (the array is doubled and every entry is re-bucketing) when
 * the next insert would push the load above 3/4. Load is count / bucket count.
 * 3/4 is the classic threshold: chains stay short, and we do not spend all of
 * our time resizing. Deletes do not shrink the table.
 */

/// <summary>
/// A hash map from <typeparamref name="TKey"/> to <typeparamref name="TValue"/>
/// using separate chaining.
/// </summary>
/// <typeparam name="TKey">The lookup key. Null keys are rejected.</typeparam>
/// <typeparam name="TValue">The value stored for a key. Null is allowed when the type allows it.</typeparam>
public sealed class HashMap<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>
    where TKey : notnull
{
    /// <summary>One key/value pair, plus the link to the next collision in the same bucket.</summary>
    private sealed class Entry
    {
        public Entry(TKey key, TValue value, int hashCode, Entry? next)
        {
            Key = key;
            Value = value;
            HashCode = hashCode;
            Next = next;
        }

        public TKey Key { get; }
        public TValue Value { get; set; }

        // The mixed hash, stored so a resize can re-bucket without calling GetHashCode again,
        // and so a lookup can reject most non-matching chain nodes before calling Equals.
        public int HashCode { get; }
        public Entry? Next { get; set; }
    }

    private const int MinimumCapacity = 4;
    private const int DefaultCapacity = 16;

    private Entry?[] _buckets;
    private readonly IEqualityComparer<TKey> _comparer;
    private int _count;
    private int _version;

    /// <summary>
    /// Creates an empty map. <paramref name="capacity"/> is a hint for how many
    /// buckets to start with; it is rounded up to a power of two and is at least 4.
    /// </summary>
    public HashMap(int capacity = DefaultCapacity, IEqualityComparer<TKey>? comparer = null)
    {
        if (capacity < 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity cannot be negative.");

        _comparer = comparer ?? EqualityComparer<TKey>.Default;
        _buckets = new Entry[NextPowerOfTwo(Math.Max(capacity, MinimumCapacity))];
    }

    /// <summary>Number of keys currently stored. O(1).</summary>
    public int Count => _count;

    /// <summary>Length of the bucket array. Grows as keys are added; not the same as <see cref="Count"/>.</summary>
    public int Capacity => _buckets.Length;

    /// <summary><see langword="true"/> when <see cref="Count"/> is zero.</summary>
    public bool IsEmpty => _count == 0;

    /// <summary>
    /// Gets or sets the value for <paramref name="key"/>.
    /// The getter throws <see cref="KeyNotFoundException"/> when the key is absent.
    /// The setter inserts the key when it is new and overwrites the value when it is already present.
    /// Average O(1).
    /// </summary>
    public TValue this[TKey key]
    {
        get
        {
            if (!TryGetValue(key, out var value))
                throw new KeyNotFoundException($"The key '{key}' was not found.");

            return value;
        }
        set => Insert(key, value, overwrite: true);
    }

    /// <summary>
    /// Adds a new key. Throws <see cref="ArgumentException"/> when <paramref name="key"/>
    /// is already in the map. Average O(1).
    /// </summary>
    public void Add(TKey key, TValue value) => Insert(key, value, overwrite: false);

    /// <summary>
    /// Adds a new key. Returns <see langword="false"/> when <paramref name="key"/>
    /// is already present and leaves the stored value unchanged. Average O(1).
    /// </summary>
    public bool TryAdd(TKey key, TValue value)
    {
        if (Find(key, out _) is not null)
            return false;

        Insert(key, value, overwrite: false);
        return true;
    }

    /// <summary>
    /// Replaces the value for an existing key. Throws <see cref="KeyNotFoundException"/>
    /// when the key is not in the map, so a typo cannot silently insert.
    /// This is the explicit edit. The indexer setter is the "insert or overwrite" form.
    /// Average O(1).
    /// </summary>
    public void Edit(TKey key, TValue newValue)
    {
        var entry = Find(key, out _);
        if (entry is null)
            throw new KeyNotFoundException($"The key '{key}' was not found.");

        entry.Value = newValue;
        _version++;
    }

    /// <summary>
    /// Looks up <paramref name="key"/>. Returns <see langword="false"/> and the default
    /// value when the key is absent. Average O(1).
    /// </summary>
    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        var entry = Find(key, out _);
        if (entry is null)
        {
            value = default;
            return false;
        }

        value = entry.Value;
        return true;
    }

    /// <summary><see langword="true"/> when <paramref name="key"/> is stored. Average O(1).</summary>
    public bool ContainsKey(TKey key) => Find(key, out _) is not null;

    /// <summary>
    /// Removes <paramref name="key"/> and its value. Returns <see langword="false"/>
    /// when the key was not stored. Average O(1), worst case O(chain length).
    /// </summary>
    public bool Delete(TKey key)
    {
        ThrowIfNullKey(key);

        int hash = Mix(_comparer.GetHashCode(key));
        int bucket = hash & (_buckets.Length - 1);

        Entry? previous = null;
        var current = _buckets[bucket];
        while (current != null)
        {
            if (current.HashCode == hash && _comparer.Equals(current.Key, key))
            {
                // Bridge the chain around the removed node. The head of the bucket
                // is a special case because it lives in the array, not in a Next field.
                if (previous is null)
                    _buckets[bucket] = current.Next;
                else
                    previous.Next = current.Next;

                _count--;
                _version++;
                return true;
            }

            previous = current;
            current = current.Next;
        }

        return false;
    }

    /// <summary>
    /// Removes every key. The bucket array keeps its current length. O(capacity).
    /// </summary>
    public void Clear()
    {
        Array.Clear(_buckets);
        _count = 0;
        _version++;
    }

    /// <summary>
    /// Walks every stored pair. Order is bucket order, then chain order, which is
    /// not insertion order and not key order. Throws if the map is mutated during the walk.
    /// </summary>
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        int version = _version;
        foreach (var head in _buckets)
        {
            for (var current = head; current != null; current = current.Next)
            {
                if (version != _version)
                    throw new InvalidOperationException("The hash map was modified during enumeration.");

                yield return new KeyValuePair<TKey, TValue>(current.Key, current.Value);
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Shared insert path for <see cref="Add"/> and the indexer setter.
    /// Grows the table before linking the new entry so the load stays at or under 3/4.
    /// </summary>
    private void Insert(TKey key, TValue value, bool overwrite)
    {
        ThrowIfNullKey(key);

        int hash = Mix(_comparer.GetHashCode(key));
        var existing = Find(key, out _, hash);
        if (existing is not null)
        {
            if (!overwrite)
                throw new ArgumentException($"The key '{key}' is already in the map.", nameof(key));

            existing.Value = value;
            _version++;
            return;
        }

        if (_count + 1 > LoadThreshold())
            Resize(_buckets.Length * 2);

        int bucket = hash & (_buckets.Length - 1);
        // Push the new entry onto the front of the chain. O(1), and order inside a bucket does not matter.
        _buckets[bucket] = new Entry(key, value, hash, _buckets[bucket]);
        _count++;
        _version++;
    }

    /// <summary>Walks the chain for <paramref name="key"/> using a hash that was already mixed.</summary>
    private Entry? Find(TKey key, out int bucket, int hash)
    {
        ThrowIfNullKey(key);
        bucket = hash & (_buckets.Length - 1);

        for (var current = _buckets[bucket]; current != null; current = current.Next)
        {
            if (current.HashCode == hash && _comparer.Equals(current.Key, key))
                return current;
        }

        return null;
    }

    private Entry? Find(TKey key, out int bucket)
    {
        ThrowIfNullKey(key);
        return Find(key, out bucket, Mix(_comparer.GetHashCode(key)));
    }

    /// <summary>count / capacity above this means the next insert must grow the table first.</summary>
    private int LoadThreshold() => _buckets.Length - (_buckets.Length >> 2);

    /// <summary>
    /// Doubles the bucket array and re-links every entry. Each entry keeps its stored
    /// hash and is pushed onto the front of its new chain. O(capacity + count).
    /// </summary>
    private void Resize(int newCapacity)
    {
        if (newCapacity <= _buckets.Length)
            throw new InvalidOperationException("The hash map cannot grow any further.");

        var fresh = new Entry[newCapacity];
        int mask = newCapacity - 1;

        foreach (var head in _buckets)
        {
            var current = head;
            while (current != null)
            {
                var next = current.Next;
                int bucket = current.HashCode & mask;
                current.Next = fresh[bucket];
                fresh[bucket] = current;
                current = next;
            }
        }

        _buckets = fresh;
    }

    private static void ThrowIfNullKey(TKey key)
    {
        // A notnull constraint does not stop a caller from passing a null reference
        // with the null-forgiving operator. Reject it here so a null never becomes a key.
        if (key is null)
            throw new ArgumentNullException(nameof(key));
    }

    /// <summary>
    /// Spreads a hash code so the low bits (the ones a power-of-two mask keeps)
    /// depend on the whole value. This is the Stafford variant of the Murmur finalizer.
    /// </summary>
    private static int Mix(int hashCode)
    {
        unchecked
        {
            uint hash = (uint)hashCode;
            hash ^= hash >> 16;
            hash *= 0x7feb352d;
            hash ^= hash >> 15;
            hash *= 0x846ca68b;
            hash ^= hash >> 16;
            return (int)hash;
        }
    }

    /// <summary>Smallest power of two that is at least <paramref name="value"/> and at least <see cref="MinimumCapacity"/>.</summary>
    private static int NextPowerOfTwo(int value)
    {
        if (value <= MinimumCapacity)
            return MinimumCapacity;

        uint capacity = (uint)value - 1;
        capacity |= capacity >> 1;
        capacity |= capacity >> 2;
        capacity |= capacity >> 4;
        capacity |= capacity >> 8;
        capacity |= capacity >> 16;
        capacity++;

        if (capacity > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value), "Capacity is too large.");

        return (int)capacity;
    }
}
