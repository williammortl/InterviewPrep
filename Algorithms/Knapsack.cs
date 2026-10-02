namespace InterviewToolkit.Algorithms;

/*
 * 0/1 knapsack
 * ------------
 * A knapsack holds a limited weight. Each item has a weight and a value, and each
 * item may be taken at most once (the "0/1"). Choose a subset whose weight fits
 * and whose total value is as large as possible.
 *
 *   capacity 7
 *   items: (weight 1, value 1), (3, 4), (4, 5), (5, 7)
 *   best:  the (3, 4) and the (4, 5)    total weight 7, total value 9
 *
 * Greedy by value/weight is not correct for 0/1. An item with a great ratio can
 * fill just enough of the bag to block two plainer items that would have been
 * worth more together. Fractional knapsack, where items may be cut, is the
 * version greedy does solve. This one does not cut items.
 *
 * Let dp[i, w] be the best value using only the first i items and a capacity of w.
 *
 *   dp[0, w] = 0                         no items, no value
 *   dp[i, 0] = 0                         no room, no value
 *   dp[i, w] = dp[i - 1, w]              skip item i - 1
 *   dp[i, w] = dp[i - 1, w - weight] + value     take it, when weight <= w
 *
 * The cell keeps the larger of "skip" and "take". The take branch reads row i - 1,
 * not row i, so the same item cannot be used twice while filling this row.
 *
 * Contrast with coin change. Coin change may reuse a denomination, so its inner
 * loop reads values that the current coin has already updated. Here the previous
 * row is the only legal source, which is also why the space-optimized 1D form
 * (not used below, because we want to recover the items) walks capacity downward:
 *
 *   for each item:
 *       for w from capacity down to item.weight:
 *           dp[w] = max(dp[w], dp[w - weight] + value)
 *
 * Walking downward means dp[w - weight] still holds the answer from the previous
 * item. Walking upward would let one item be packed several times.
 *
 * After the table is full, dp[n, capacity] is the best value. The chosen items
 * are recovered by walking back from that cell: if dp[i, w] differs from
 * dp[i - 1, w], item i - 1 was taken, and the remaining capacity is w - weight.
 * Ties prefer skipping, because a cell is replaced only when taking is strictly
 * better. The subset is therefore deterministic for a given item order.
 *
 * Time is O(n * capacity). Extra memory is O(n * capacity) for the table.
 * That pseudo-polynomial cost is the important limitation: capacity 10 is fine,
 * capacity 10^9 with large n is not, even though the input itself would be small.
 */

/// <summary>
/// 0/1 knapsack: each item is taken at most once, maximizing value within a weight limit.
/// </summary>
public static class Knapsack
{
    /// <summary>
    /// One candidate item. Weight must be zero or positive. Value may be negative,
    /// and a negative value is never worth taking because skipping it is better.
    /// </summary>
    public readonly record struct Item
    {
        /// <summary>Creates an item and rejects a negative weight.</summary>
        public Item(int weight, int value)
        {
            if (weight < 0)
                throw new ArgumentOutOfRangeException(nameof(weight), "Weight cannot be negative.");

            Weight = weight;
            Value = value;
        }

        public int Weight { get; }
        public int Value { get; }
    }

    /// <summary>
    /// The best total value and the indexes of one subset that achieves it,
    /// in increasing input order.
    /// </summary>
    public readonly record struct Result(int TotalValue, IReadOnlyList<int> ChosenIndexes);

    /// <summary>
    /// Chooses a highest-value subset of <paramref name="items"/> whose weights
    /// sum to at most <paramref name="capacity"/>.
    /// </summary>
    public static Result Solve(IReadOnlyList<Item> items, int capacity)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (capacity < 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity cannot be negative.");

        int count = items.Count;
        if (count == 0 || capacity == 0)
            return new Result(0, Array.Empty<int>());

        // Row i uses only items[0..i). Column w is a capacity. Both axes have a
        // zero border so the recurrence can read "no items" and "no room" without
        // a special case inside the loop.
        var best = new int[count + 1, capacity + 1];

        for (int itemNumber = 1; itemNumber <= count; itemNumber++)
        {
            Item item = items[itemNumber - 1];
            for (int weight = 0; weight <= capacity; weight++)
            {
                int skip = best[itemNumber - 1, weight];
                best[itemNumber, weight] = skip;

                if (item.Weight > weight)
                    continue;

                int take = best[itemNumber - 1, weight - item.Weight] + item.Value;
                if (take > skip)
                    best[itemNumber, weight] = take;
            }
        }

        var chosen = new List<int>();
        int remaining = capacity;
        for (int itemNumber = count; itemNumber >= 1; itemNumber--)
        {
            // Equal cells mean the skip branch was kept (take is applied only when
            // it is strictly greater), so this item is not in the chosen subset.
            if (best[itemNumber, remaining] == best[itemNumber - 1, remaining])
                continue;

            int index = itemNumber - 1;
            chosen.Add(index);
            remaining -= items[index].Weight;
        }

        // The walk above discovers later items first. Reverse to report input order.
        chosen.Reverse();
        return new Result(best[count, capacity], chosen);
    }
}
