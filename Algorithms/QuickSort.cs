namespace InterviewToolkit.Algorithms;

/*
 * Quick sort
 * ----------
 * Also divide and conquer, but the work happens before the recursive calls
 * instead of after them. Pick a pivot value, then rearrange the range so that:
 *
 *   [ < pivot | == pivot | > pivot ]
 *
 * Everything left of the equal band belongs before the pivot, everything right
 * of it belongs after. The equal band is already in its final place and is not
 * sorted again. Recurse on the two outer bands.
 *
 *        [3, 1, 4, 1, 5, 2]     pivot 4
 *                 |
 *        [3, 1, 1, 2 | 4 | 5]
 *          /              \
 *        ...              already one element
 *
 * Partitioning is Dijkstra's three-way split (the Dutch national flag). A plain
 * two-way split treats a run of equal keys as work still to do, so an array of
 * identical values degrades to O(n^2). Three-way split finishes that run in one
 * pass.
 *
 * Average time is O(n log n). The worst split (pivot is always the minimum or
 * maximum, and values are unique) is O(n^2). Extra memory is O(log n) for the
 * call stack: the recursive call is always the smaller side, and the larger
 * side is handled by the loop, so a lopsided pivot cannot make the stack O(n).
 *
 * Quick sort is not stable. Equal keys can be reordered by the swaps, which is
 * the trade for sorting in place. Use merge sort when original order of ties matters.
 */

/// <summary>
/// In-place, generic quick sort using a three-way partition.
/// </summary>
public static class QuickSort
{
    /// <summary>
    /// Sorts <paramref name="items"/> in place. Not stable.
    /// </summary>
    /// <typeparam name="T">The element type. It does not have to be comparable when a comparer is supplied.</typeparam>
    /// <param name="items">Writable random-access list. An array is an <see cref="IList{T}"/>.</param>
    /// <param name="comparer">
    /// Ordering to use. Null means <see cref="Comparer{T}.Default"/>.
    /// </param>
    public static void Sort<T>(IList<T> items, IComparer<T>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count < 2)
            return;

        SortRange(items, 0, items.Count - 1, comparer ?? Comparer<T>.Default);
    }

    /// <summary>
    /// Sorts the inclusive index range <paramref name="low"/> .. <paramref name="high"/>.
    /// </summary>
    private static void SortRange<T>(IList<T> items, int low, int high, IComparer<T> comparer)
    {
        // The loop covers the larger side. Recursion covers only the smaller side,
        // so the depth is O(log n) even when a pivot peels off a single element.
        while (low < high)
        {
            (int equalStart, int equalEnd) = Partition(items, low, high, comparer);

            if (equalStart - low < high - equalEnd)
            {
                SortRange(items, low, equalStart - 1, comparer);
                low = equalEnd + 1;
            }
            else
            {
                SortRange(items, equalEnd + 1, high, comparer);
                high = equalStart - 1;
            }
        }
    }

    /*
     * Rearranges items[low..high] around a pivot value:
     *
     *   low                         high
     *    | < pivot | == pivot | > pivot |
     *    ^          ^          ^
     *   less        i       greater
     *
     * The pivot is the value in the middle of the range. Middle is a hedge against
     * already-sorted input: the first or last element is the worst pivot on a
     * sorted list. After partitioning, that original index no longer matters.
     *
     * i walks forward. A small value is swapped into the less-than region and both
     * cursors advance. A large value is swapped into the greater-than region, and
     * i stays put because the value that arrived has not been looked at yet.
     * An equal value is left where it is.
     *
     * Returns the inclusive index range of the equal band.
     */
    private static (int EqualStart, int EqualEnd) Partition<T>(
        IList<T> items,
        int low,
        int high,
        IComparer<T> comparer)
    {
        T pivot = items[low + ((high - low) / 2)];
        int less = low;
        int greater = high;
        int index = low;

        while (index <= greater)
        {
            int comparison = comparer.Compare(items[index], pivot);
            if (comparison < 0)
            {
                Swap(items, less, index);
                less++;
                index++;
            }
            else if (comparison > 0)
            {
                Swap(items, index, greater);
                greater--;
            }
            else
            {
                index++;
            }
        }

        return (less, greater);
    }

    private static void Swap<T>(IList<T> items, int left, int right)
    {
        if (left == right)
            return;

        (items[left], items[right]) = (items[right], items[left]);
    }
}
