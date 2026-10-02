namespace InterviewToolkit.Algorithms;

/*
 * Merge sort
 * ----------
 * Divide and conquer. Split the list in half, sort each half, then merge the
 * two sorted halves into one sorted run.
 *
 *        [3, 1, 4, 2]
 *          /      \
 *     [3, 1]      [4, 2]
 *      /   \      /   \
 *    [3]   [1]  [4]   [2]
 *      \   /      \   /
 *     [1, 3]      [2, 4]
 *          \      /
 *        [1, 2, 3, 4]
 *
 * The merge step is the whole algorithm. Two fingers walk the sorted halves,
 * and the smaller head is written next:
 *
 *   left:  [1, 3 |]     right: [2, 4 |]
 *   out:   [1, 2, 3, 4]
 *
 * When the two heads compare equal, the one from the left half is written
 * first. The left half holds earlier original items (it was the left side of
 * the previous split), so equal keys keep their original order. That is why
 * merge sort is stable.
 *
 * Time is O(n log n) at every input shape: each level does O(n) merging work
 * and there are O(log n) levels. Extra memory is O(n) for the merge buffer.
 * The call stack is O(log n) deep.
 */

/// <summary>
/// Stable, generic merge sort. Sorts any <see cref="IList{T}"/> that supports
/// random access and writing by index.
/// </summary>
public static class MergeSort
{
    /// <summary>
    /// Sorts <paramref name="items"/> in place into ascending order.
    /// </summary>
    /// <typeparam name="T">The element type. It does not have to be comparable when a comparer is supplied.</typeparam>
    /// <param name="items">The list to sort. Must be writable. An array is an <see cref="IList{T}"/>.</param>
    /// <param name="comparer">
    /// Ordering to use. Null means <see cref="Comparer{T}.Default"/>, which throws
    /// when <typeparamref name="T"/> is not comparable.
    /// </param>
    public static void Sort<T>(IList<T> items, IComparer<T>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count < 2)
            return;

        // Copy into an array so the recursive merge does not pay for an interface
        // call on every read, then write the sorted array back. The buffer is
        // allocated once and reused by every level.
        var ordering = comparer ?? Comparer<T>.Default;
        var source = new T[items.Count];
        for (int index = 0; index < source.Length; index++)
            source[index] = items[index];

        var buffer = new T[source.Length];
        SortRange(source, buffer, 0, source.Length, ordering);

        for (int index = 0; index < source.Length; index++)
            items[index] = source[index];
    }

    /// <summary>
    /// Sorts <paramref name="items"/> from <paramref name="start"/> for <paramref name="length"/> elements.
    /// <paramref name="buffer"/> is scratch space of the same length as <paramref name="items"/>.
    /// </summary>
    private static void SortRange<T>(T[] items, T[] buffer, int start, int length, IComparer<T> comparer)
    {
        // Zero or one element is already sorted. This is the base of the recursion.
        if (length < 2)
            return;

        int leftLength = length / 2;
        int rightLength = length - leftLength;

        SortRange(items, buffer, start, leftLength, comparer);
        SortRange(items, buffer, start + leftLength, rightLength, comparer);
        Merge(items, buffer, start, leftLength, rightLength, comparer);
    }

    /// <summary>
    /// Merges the sorted run <c>[start, start + leftLength)</c> with the sorted run
    /// that follows it. The merged run is written through <paramref name="buffer"/>
    /// and then copied back over the same span of <paramref name="items"/>.
    /// </summary>
    private static void Merge<T>(
        T[] items,
        T[] buffer,
        int start,
        int leftLength,
        int rightLength,
        IComparer<T> comparer)
    {
        int left = start;
        int leftEnd = start + leftLength;
        int right = leftEnd;
        int rightEnd = leftEnd + rightLength;
        int write = start;

        while (left < leftEnd && right < rightEnd)
        {
            // <=, not <, is the stability rule. An equal key from the left half
            // came from an earlier position in the original list, so it goes first.
            if (comparer.Compare(items[left], items[right]) <= 0)
                buffer[write++] = items[left++];
            else
                buffer[write++] = items[right++];
        }

        // One side is empty. The other side is already sorted and all of it is
        // greater than what has been written, so copy the tail straight across.
        while (left < leftEnd)
            buffer[write++] = items[left++];

        while (right < rightEnd)
            buffer[write++] = items[right++];

        for (int index = start; index < rightEnd; index++)
            items[index] = buffer[index];
    }
}
