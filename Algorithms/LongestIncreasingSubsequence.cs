namespace InterviewToolkit.Algorithms;

/*
 * Longest increasing subsequence
 * ------------------------------
 * A subsequence keeps the original order but may skip elements. It does not have
 * to be contiguous. In
 *
 *   10, 9, 2, 5, 3, 7, 101, 18
 *
 * one longest increasing subsequence is 2, 3, 7, 18. Length 4.
 * 2, 5, 7, 101 is another of the same length. 10, 101 is increasing but shorter.
 *
 * The O(n^2) recurrence is the straightforward one. Let length[i] be the longest
 * increasing subsequence that ends at index i:
 *
 *   length[i] = 1 + max(length[j] for every j < i with value[j] < value[i])
 *   length[i] = 1 when no such j exists
 *
 * The answer is the maximum length[i]. It is correct and easy to see, and it does
 * quadratic work because every pair (j, i) is considered.
 *
 * The method below is the O(n log n) version, usually described as patience sorting.
 * It keeps this table:
 *
 *   tail[len] = the smallest ending value of any increasing subsequence of length len + 1
 *               found so far
 *
 * tail is always sorted. For each new value, binary search finds the first tail
 * that is greater than or equal to it:
 *
 *   - Replacing that tail with the new, smaller value makes future extensions
 *     easier, without changing the best length.
 *   - If the new value is greater than every tail, it extends the longest
 *     subsequence by one, and the table grows.
 *
 * Trace for 10, 9, 2, 5, 3, 7, 101, 18. The numbers shown are values; the code
 * actually stores indexes so the subsequence can be rebuilt.
 *
 *   10            tails: 10
 *   9             tails: 9            (9 replaces 10; length stays 1)
 *   2             tails: 2
 *   5             tails: 2, 5
 *   3             tails: 2, 3         (3 replaces 5)
 *   7             tails: 2, 3, 7
 *   101           tails: 2, 3, 7, 101
 *   18            tails: 2, 3, 7, 18
 *
 * tails is not itself the subsequence. 2, 3, 7, 18 happens to be one, but that
 * is not guaranteed: a later smaller tail can come from a different chain than
 * the longer tails beside it. To recover one real subsequence, every element
 * remembers the index of the tail it extended (the previous length). Following
 * those links from the last tail produces the elements in reverse, and reversing
 * them restores input order.
 *
 * The comparison is strict. Equal values do not extend the subsequence; an equal
 * value replaces a tail instead. Changing the binary search from "first tail
 * greater than or equal" to "first tail strictly greater" would produce a longest
 * non-decreasing subsequence instead.
 *
 * Time is O(n log n) for the binary searches. Extra memory is O(n).
 */

/// <summary>
/// Longest strictly increasing subsequence, in original order.
/// </summary>
public static class LongestIncreasingSubsequence
{
    /// <summary>
    /// Returns one longest strictly increasing subsequence of <paramref name="items"/>.
    /// When several have the same length, the one this method returns is determined
    /// by the tails table above, not by a secondary rule such as the earliest start.
    /// An empty input returns an empty list. A strictly decreasing input returns
    /// a single element, because length 1 is optimal.
    /// </summary>
    /// <typeparam name="T">The element type. It does not have to be comparable when a comparer is supplied.</typeparam>
    public static IReadOnlyList<T> Find<T>(IReadOnlyList<T> items, IComparer<T>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
            return Array.Empty<T>();

        var ordering = comparer ?? Comparer<T>.Default;

        // tailIndex[len] is the index in `items` of the smallest tail of any
        // increasing subsequence of length len + 1 seen so far.
        var tailIndex = new int[items.Count];
        // previous[i] is the index of the element before items[i] in the subsequence
        // that ends at i. -1 means items[i] starts its subsequence.
        var previous = new int[items.Count];
        int length = 0;

        for (int index = 0; index < items.Count; index++)
        {
            int position = FirstTailNotLessThan(tailIndex, length, items, items[index], ordering);
            previous[index] = position == 0 ? -1 : tailIndex[position - 1];
            tailIndex[position] = index;
            if (position == length)
                length++;
        }

        var chosen = new T[length];
        int cursor = tailIndex[length - 1];
        for (int place = length - 1; place >= 0; place--)
        {
            chosen[place] = items[cursor];
            cursor = previous[cursor];
        }

        return chosen;
    }

    /// <summary>
    /// Binary search over the first <paramref name="length"/> tails for the leftmost
    /// tail that is greater than or equal to <paramref name="candidate"/>.
    /// Returns <paramref name="length"/> when the candidate is greater than every tail,
    /// which means the candidate extends the longest subsequence.
    /// </summary>
    private static int FirstTailNotLessThan<T>(
        int[] tailIndex,
        int length,
        IReadOnlyList<T> items,
        T candidate,
        IComparer<T> comparer)
    {
        int low = 0;
        int high = length;
        while (low < high)
        {
            int mid = low + ((high - low) / 2);
            if (comparer.Compare(items[tailIndex[mid]], candidate) < 0)
                low = mid + 1;
            else
                high = mid;
        }

        return low;
    }
}
