namespace InterviewToolkit.Algorithms;

/*
 * Coin change
 * -----------
 * Two related questions about an unlimited supply of coin denominations:
 *
 *   1. What is the fewest coins that sum to an amount?        (MakeChange)
 *   2. How many distinct combinations sum to that amount?     (CountCombinations)
 *
 * Greedy (always take the largest coin that fits) is not correct for arbitrary
 * denominations. With coins 1, 3, and 4, and amount 6:
 *
 *   greedy: 4 + 1 + 1 = 3 coins
 *   optimal: 3 + 3     = 2 coins
 *
 * Dynamic programming answers it by solving every smaller amount first.
 *
 *   dp[a] = fewest coins that sum to a
 *   dp[0] = 0                         (the empty selection)
 *   dp[a] = min over coins c <= a of (dp[a - c] + 1)
 *
 * Taking coin c leaves the sub-problem "make a - c", which is already solved
 * because we fill the table from 0 upward. A cell that is never improved cannot
 * be made. The worked example, coins {1, 3, 4}:
 *
 *   amount:   0  1  2  3  4  5  6
 *   coins:    0  1  2  1  1  2  2
 *   last coin:-  1  1  3  4  4  3
 *
 * Walking backward from 6 (coin 3, then coin 3) rebuilds one optimal selection.
 * Another selection with the same count may exist; the table keeps only one,
 * the one produced by trying the denominations in the order they were given
 * and replacing a cell only when the count strictly improves.
 *
 * This is unbounded: each denomination may be used any number of times. That is
 * why the inner loop may use dp[a - c] after coin c has already been considered
 * for smaller amounts. The 0/1 knapsack in Knapsack.cs is the opposite rule
 * (each item at most once), which is why its capacity loop runs backward.
 *
 * Counting combinations is a different recurrence. Order does not matter:
 * 1+3 and 3+1 are the same combination. Iterate denominations on the outside
 * and amounts on the inside so each denomination is considered in one phase
 * and never reordered:
 *
 *   ways[0] = 1
 *   for each coin c:
 *       for a from c to amount:
 *           ways[a] += ways[a - c]
 *
 * Swapping those loops would count permutations instead (1+3 distinct from 3+1).
 *
 * Time is O(amount * number of coins) for both. Extra memory is O(amount).
 */

/// <summary>
/// Unbounded coin-change: fewest coins that sum to an amount, and the number of combinations.
/// </summary>
public static class CoinChange
{
    /// <summary>
    /// One optimal way to make an amount. <see cref="Count"/> is -1 when the amount
    /// cannot be made. <see cref="Coins"/> is then empty. When the amount is 0,
    /// <see cref="Count"/> is 0 and <see cref="Coins"/> is empty.
    /// </summary>
    public readonly record struct Solution(int Count, IReadOnlyList<int> Coins)
    {
        /// <summary><see langword="true"/> when <see cref="Count"/> is zero or positive.</summary>
        public bool IsPossible => Count >= 0;
    }

    /// <summary>
    /// Fewest coins from <paramref name="coins"/> that sum to <paramref name="amount"/>.
    /// Each denomination may be used any number of times. Denominations must be positive.
    /// </summary>
    public static Solution MakeChange(IReadOnlyList<int> coins, int amount)
    {
        ArgumentNullException.ThrowIfNull(coins);
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount cannot be negative.");

        ValidateDenominations(coins);

        if (amount == 0)
            return new Solution(0, Array.Empty<int>());

        // amount + 1 is a sentinel meaning "not yet possible". No real selection
        // needs more coins than the amount itself when a coin of 1 exists, and
        // when it does not, any impossible cell stays at the sentinel.
        int impossible = amount + 1;
        var fewest = new int[amount + 1];
        var lastCoin = new int[amount + 1];
        Array.Fill(fewest, impossible);
        fewest[0] = 0;

        for (int current = 1; current <= amount; current++)
        {
            for (int coinIndex = 0; coinIndex < coins.Count; coinIndex++)
            {
                int coin = coins[coinIndex];
                int remaining = current - coin;
                if (remaining < 0 || fewest[remaining] == impossible)
                    continue;

                int candidate = fewest[remaining] + 1;
                if (candidate < fewest[current])
                {
                    fewest[current] = candidate;
                    lastCoin[current] = coin;
                }
            }
        }

        if (fewest[amount] == impossible)
            return new Solution(-1, Array.Empty<int>());

        // Peel one coin off the amount at a time. Each step lands on a smaller
        // amount whose last coin was recorded the same way, so this reaches 0.
        var chosen = new List<int>(fewest[amount]);
        for (int current = amount; current > 0; current -= lastCoin[current])
            chosen.Add(lastCoin[current]);

        return new Solution(fewest[amount], chosen);
    }

    /// <summary>
    /// Number of combinations of <paramref name="coins"/> that sum to <paramref name="amount"/>.
    /// Order does not matter. Denominations must be positive and unique; a repeated
    /// denomination would be treated as a second coin type and would over-count.
    /// Amount 0 has one combination: take nothing.
    /// </summary>
    public static long CountCombinations(IReadOnlyList<int> coins, int amount)
    {
        ArgumentNullException.ThrowIfNull(coins);
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount cannot be negative.");

        ValidateDenominations(coins);
        RejectDuplicateDenominations(coins);

        var ways = new long[amount + 1];
        ways[0] = 1;

        for (int coinIndex = 0; coinIndex < coins.Count; coinIndex++)
        {
            int coin = coins[coinIndex];
            // Starting at `coin` avoids a negative index. Adding ways[a - coin]
            // folds in every combination of the coins considered so far that
            // fills the gap, including combinations that already used this coin.
            for (int current = coin; current <= amount; current++)
                ways[current] += ways[current - coin];
        }

        return ways[amount];
    }

    private static void ValidateDenominations(IReadOnlyList<int> coins)
    {
        for (int index = 0; index < coins.Count; index++)
        {
            if (coins[index] <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(coins),
                    "Every denomination must be positive. Zero would not change the amount, and a negative coin is not a denomination.");
            }
        }
    }

    private static void RejectDuplicateDenominations(IReadOnlyList<int> coins)
    {
        var seen = new HashSet<int>(coins.Count);
        for (int index = 0; index < coins.Count; index++)
        {
            if (!seen.Add(coins[index]))
            {
                throw new ArgumentException(
                    $"Denomination {coins[index]} is listed more than once. Combinations treat each listed value as its own coin type.",
                    nameof(coins));
            }
        }
    }
}
