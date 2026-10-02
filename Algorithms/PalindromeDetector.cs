namespace InterviewToolkit.Algorithms;

/*
 * Palindrome detector
 * -------------------
 * A palindrome reads the same forwards and backwards.
 *
 *   r a c e c a r
 *   ^           ^
 *     ^       ^
 *       ^   ^
 *         ^
 *
 * Two pointers are enough. One starts at the first position, the other at the
 * last. While they have not met, the values they point at must be equal. Each
 * match moves them one step toward the middle. The first mismatch means the
 * sequence is not a palindrome.
 *
 * Why this is complete: if position i must equal position n - 1 - i for every i,
 * checking from the outside in covers every required pair exactly once. The
 * middle character of an odd-length sequence is paired with itself and does not
 * need a comparison.
 *
 * Time is O(n). Extra memory is O(1). No copy and no reverse-and-compare, which
 * would spend O(n) extra memory to do the same check.
 *
 * Strings have a second, looser question: "would this be a palindrome if we
 * ignored case and punctuation?" That is the same two pointers, except a pointer
 * skips characters that are not letters or digits, and the comparison folds case.
 * "A man, a plan, a canal: Panama" is the usual example.
 *
 * Case folding here is per char via ToLowerInvariant. That is the interview
 * version. It is not a full linguistic case fold (for example German ß).
 */

/// <summary>
/// Detects palindromes in a sequence, and the common normalized form of a string.
/// </summary>
public static class PalindromeDetector
{
    /// <summary>
    /// <see langword="true"/> when <paramref name="sequence"/> equals its own reverse.
    /// An empty sequence and a one-element sequence are palindromes.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    public static bool IsPalindrome<T>(IReadOnlyList<T> sequence, IEqualityComparer<T>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(sequence);

        var equality = comparer ?? EqualityComparer<T>.Default;
        int left = 0;
        int right = sequence.Count - 1;

        while (left < right)
        {
            if (!equality.Equals(sequence[left], sequence[right]))
                return false;

            left++;
            right--;
        }

        return true;
    }

    /// <summary>
    /// <see langword="true"/> when <paramref name="text"/> equals its own reverse,
    /// comparing characters exactly. "AbA" is a palindrome. "Ab" is not.
    /// </summary>
    public static bool IsPalindrome(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        int left = 0;
        int right = text.Length - 1;
        while (left < right)
        {
            if (text[left] != text[right])
                return false;

            left++;
            right--;
        }

        return true;
    }

    /// <summary>
    /// <see langword="true"/> when the letters and digits in <paramref name="text"/>
    /// form a palindrome, ignoring case and ignoring every other character.
    /// </summary>
    /// <remarks>
    /// The pointers still walk inward, but each one first skips anything that is
    /// not a letter or digit. The characters that remain are compared after
    /// invariant lower-casing. A string with no letters or digits is a palindrome:
    /// both pointers meet immediately and there is nothing left to contradict it.
    /// </remarks>
    public static bool IsNormalizedPalindrome(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        int left = 0;
        int right = text.Length - 1;
        while (left < right)
        {
            while (left < right && !char.IsLetterOrDigit(text[left]))
                left++;

            while (left < right && !char.IsLetterOrDigit(text[right]))
                right--;

            if (char.ToLowerInvariant(text[left]) != char.ToLowerInvariant(text[right]))
                return false;

            left++;
            right--;
        }

        return true;
    }
}
