using InterviewToolkit.Algorithms;
using InterviewToolkit.DataStructures;

namespace InterviewToolkit;

/// <summary>
/// Walks each data structure and algorithm and checks the results. A failed check throws,
/// so a clean run means the operations below behaved as expected.
/// </summary>
internal static class Program
{
    private static void Main()
    {
        DemonstrateTree();
        DemonstrateDoublyLinkedList();
        DemonstrateHeap();
        DemonstratePriorityQueue();
        DemonstrateHashMap();
        DemonstrateMergeSort();
        DemonstrateQuickSort();
        DemonstrateConvexHull();
        DemonstratePalindromeDetector();
        DemonstrateCoinChange();
        DemonstrateLongestIncreasingSubsequence();
        DemonstrateKnapsack();
        Console.WriteLine();
        Console.WriteLine("All data structure and algorithm checks passed.");
    }

    private static void DemonstrateTree()
    {
        Console.WriteLine("=== Tree ===");

        //            A
        //          /   \
        //         B     C
        //        / \
        //       D   E
        var tree = new Tree<string>();
        var a = tree.AddRoot("A");
        var b = tree.AddChild(a, "B");
        var c = tree.AddChild(a, "C");
        var d = tree.AddChild(b, "D");
        tree.AddChild(b, "E");

        Console.WriteLine("  built:");
        PrintTree(tree.Root);
        ExpectSequence("preorder", tree.TraversePreOrder().Select(node => node.Value), "A", "B", "D", "E", "C");
        ExpectSequence("level order", tree.TraverseLevelOrder().Select(node => node.Value), "A", "B", "C", "D", "E");
        Expect(tree.Count == 5, "count after build");

        tree.Edit(d, "D2");
        Expect(tree.Find("D2") == d, "edit by node");
        Expect(tree.EditFirst("E", "E2"), "edit by value");
        Console.WriteLine("  after editing D -> D2 and E -> E2:");
        PrintTree(tree.Root);

        // Promote B: D2 and E2 take B's place under A, and C stays the last child.
        tree.DeleteAndPromoteChildren(b);
        Expect(!b.IsAttached, "promoted node is detached");
        ExpectSequence("after promoting B", tree.TraverseLevelOrder().Select(node => node.Value), "A", "D2", "E2", "C");
        Expect(tree.Count == 4, "count after promote");
        Console.WriteLine("  after promoting B:");
        PrintTree(tree.Root);

        tree.Delete(c);
        ExpectSequence("after deleting C", tree.TraversePreOrder().Select(node => node.Value), "A", "D2", "E2");
        Expect(tree.Count == 3, "count after subtree delete");

        ExpectThrows<InvalidOperationException>("edit a deleted node", () => tree.Edit(c, "nope"));

        var other = new Tree<string>();
        var foreign = other.AddRoot("Z");
        ExpectThrows<InvalidOperationException>("cross-tree add", () => tree.AddChild(foreign, "nope"));

        tree.Clear();
        Expect(tree.IsEmpty && tree.Root is null, "clear");
        Console.WriteLine();
    }

    private static void DemonstrateDoublyLinkedList()
    {
        Console.WriteLine("=== Doubly linked list ===");

        var list = new DoublyLinkedList<int>();
        list.AddLast(10);
        list.AddLast(20);
        list.AddLast(30);
        list.AddLast(40);

        Expect(list.Length == 4, "length");
        Expect(list[0] == 10 && list[3] == 40, "indexer get, including the tail");

        list[1] = 25;
        list.Edit(2, 35);
        ExpectSequence("after indexer set and Edit", list, 10, 25, 35, 40);

        list.Insert(0, 5);
        list.Insert(list.Length, 50);
        list.Insert(3, 30);
        ExpectSequence("after inserts", list, 5, 10, 25, 30, 35, 40, 50);
        Expect(list.Length == 7, "length after inserts");

        list.DeleteAt(0);
        list.DeleteAt(list.Length - 1);
        list.DeleteAt(2);
        Expect(list.Delete(25), "delete by value");
        Expect(!list.Delete(25), "second delete of the same value finds nothing");
        ExpectSequence("after deletes", list, 10, 35, 40);
        Expect(list.Length == 3, "length after deletes");

        list.Reverse();
        ExpectSequence("reversed", list, 40, 35, 10);
        Expect(list[0] == 40 && list[list.Length - 1] == 10, "ends after reverse");

        list.Reverse();
        ExpectSequence("reversed back", list, 10, 35, 40);

        ExpectThrows<ArgumentOutOfRangeException>("index past the end", () => _ = list[list.Length]);
        Console.WriteLine();
    }

    private static void DemonstrateHeap()
    {
        Console.WriteLine("=== Heap ===");

        var min = new Heap<int>(HeapOrder.Min);
        foreach (var value in new[] { 5, 1, 8, 3, 1 })
            min.Insert(value);

        Expect(min.Peek() == 1, "min-heap peek");
        ExpectSequence("min-heap extract", Drain(min), 1, 1, 3, 5, 8);

        var edited = new Heap<int>(HeapOrder.Min);
        edited.Insert(9);
        edited.Insert(4);
        edited.Insert(7);
        Expect(edited.Edit(9, 0), "edit a present item");
        Expect(!edited.Edit(9, 1), "edit misses an item that was already replaced");
        ExpectSequence("min-heap after editing 9 to 0", Drain(edited), 0, 4, 7);

        var built = new Heap<int>(new[] { 4, 1, 3, 2 }, HeapOrder.Min);
        ExpectSequence("heapify", Drain(built), 1, 2, 3, 4);

        var max = new Heap<int>(new[] { 2, 9, 4 }, HeapOrder.Max);
        Expect(max.Peek() == 9, "max-heap peek");
        ExpectSequence("max-heap extract", Drain(max), 9, 4, 2);
        Console.WriteLine();
    }

    private static void DemonstratePriorityQueue()
    {
        Console.WriteLine("=== Priority queue ===");

        // Lower number leaves first. The two priority-2 items stay in enqueue order.
        var queue = new DataStructures.PriorityQueue<string, int>();
        queue.Enqueue("write tests", 2);
        queue.Enqueue("fix bug", 0);
        queue.Enqueue("ship", 1);
        queue.Enqueue("refactor", 2);

        Expect(queue.Peek() == "fix bug" && queue.PeekPriority() == 0, "peek");
        ExpectSequence(
            "dequeue (FIFO ties)",
            Drain(queue),
            "fix bug",
            "ship",
            "write tests",
            "refactor");

        var highestFirst = new DataStructures.PriorityQueue<string, int>(Comparer<int>.Create((left, right) => right.CompareTo(left)));
        highestFirst.Enqueue("low", 1);
        highestFirst.Enqueue("high", 5);
        highestFirst.Enqueue("mid", 3);
        ExpectSequence("reversed comparer", Drain(highestFirst), "high", "mid", "low");
        Console.WriteLine();
    }

    private static void DemonstrateHashMap()
    {
        Console.WriteLine("=== Hash map ===");

        var map = new HashMap<string, int>();
        map.Add("alpha", 1);
        map["beta"] = 2;
        Expect(map.Count == 2 && map["alpha"] == 1 && map.TryGetValue("beta", out var beta) && beta == 2, "add and get");

        map.Edit("alpha", 10);
        Expect(map["alpha"] == 10, "edit");
        map["beta"] = 20;
        Expect(map["beta"] == 20, "indexer overwrite");
        ExpectThrows<KeyNotFoundException>("edit a missing key", () => map.Edit("missing", 1));
        ExpectThrows<ArgumentException>("add a duplicate key", () => map.Add("alpha", 0));

        Expect(map.Delete("beta"), "delete");
        Expect(!map.ContainsKey("beta") && map.Count == 1, "deleted key is gone");
        Expect(!map.Delete("beta"), "delete is idempotent in its return value");

        // Every key hashes to the same bucket, so all three share one chain.
        // Delete the middle one and both neighbors must still be reachable.
        var collisions = new HashMap<string, int>(capacity: 4, comparer: new SameHashComparer());
        collisions.Add("one", 1);
        collisions.Add("two", 2);
        collisions.Add("three", 3);
        Expect(collisions.Capacity == 4, "starting capacity stays a power of two");
        Expect(collisions.Delete("two"), "delete from the middle of a collision chain");
        Expect(collisions["one"] == 1 && collisions["three"] == 3 && !collisions.ContainsKey("two"), "chain stayed linked");

        var grown = new HashMap<int, int>(capacity: 4);
        for (int key = 0; key < 20; key++)
            grown.Add(key, key * 10);

        Expect(grown.Count == 20 && grown.Capacity > 4, "table grew");
        for (int key = 0; key < 20; key++)
            Expect(grown[key] == key * 10, $"value survived resize ({key})");

        for (int key = 0; key < 20; key += 2)
            grown.Delete(key);

        Expect(grown.Count == 10, "half deleted");
        for (int key = 1; key < 20; key += 2)
            Expect(grown[key] == key * 10, $"odd key survived delete ({key})");

        Console.WriteLine("  resize, collision chain, edit, and delete checks passed");
        Console.WriteLine();
    }

    private static void DemonstrateMergeSort()
    {
        Console.WriteLine("=== Merge sort ===");

        var numbers = new[] { 3, 1, 4, 1, 5, 9, 2, 6 };
        MergeSort.Sort(numbers);
        ExpectSequence("sorted", numbers, 1, 1, 2, 3, 4, 5, 6, 9);

        // The second field records original order. Sorting only by the first field
        // must keep (1, 0) ahead of (1, 1), and (2, 0) ahead of (2, 1).
        var ties = new (int Value, int Order)[] { (2, 0), (1, 0), (2, 1), (1, 1) };
        MergeSort.Sort(ties, Comparer<(int Value, int Order)>.Create((left, right) => left.Value.CompareTo(right.Value)));
        ExpectSequence("stable ties", ties, (1, 0), (1, 1), (2, 0), (2, 1));
        Console.WriteLine();
    }

    private static void DemonstrateQuickSort()
    {
        Console.WriteLine("=== Quick sort ===");

        var numbers = new[] { 4, 2, 4, 1, 3, 4 };
        QuickSort.Sort(numbers);
        ExpectSequence("sorted, duplicates grouped", numbers, 1, 2, 3, 4, 4, 4);

        var descending = new[] { 1, 3, 2 };
        QuickSort.Sort(descending, Comparer<int>.Create((left, right) => right.CompareTo(left)));
        ExpectSequence("descending comparer", descending, 3, 2, 1);
        Console.WriteLine();
    }

    private static void DemonstrateConvexHull()
    {
        Console.WriteLine("=== Convex hull ===");

        // Rectangle corners, plus a point on the bottom edge and a point inside.
        // Neither extra point is a corner. The duplicate origin is ignored.
        var hull = ConvexHull.Compute(new[]
        {
            new ConvexHull.Point(0, 0),
            new ConvexHull.Point(1, 0),
            new ConvexHull.Point(4, 0),
            new ConvexHull.Point(4, 3),
            new ConvexHull.Point(0, 3),
            new ConvexHull.Point(2, 1),
            new ConvexHull.Point(0, 0)
        });

        ExpectSequence(
            "counter-clockwise corners",
            hull,
            new ConvexHull.Point(0, 0),
            new ConvexHull.Point(4, 0),
            new ConvexHull.Point(4, 3),
            new ConvexHull.Point(0, 3));
        Console.WriteLine();
    }

    private static void DemonstratePalindromeDetector()
    {
        Console.WriteLine("=== Palindrome detector ===");

        Expect(PalindromeDetector.IsPalindrome("racecar"), "racecar");
        Expect(!PalindromeDetector.IsPalindrome("hello"), "hello");
        Expect(!PalindromeDetector.IsPalindrome("RaceCar"), "RaceCar is not a strict palindrome");
        Expect(PalindromeDetector.IsNormalizedPalindrome("A man, a plan, a canal: Panama"), "normalized sentence");
        Expect(!PalindromeDetector.IsNormalizedPalindrome("race a car"), "race a car");
        Expect(PalindromeDetector.IsPalindrome(new[] { 1, 2, 3, 2, 1 }), "integer sequence");
        Expect(!PalindromeDetector.IsPalindrome(new[] { 1, 2, 3 }), "integer sequence that is not");
        Console.WriteLine("  strict, normalized, and generic checks passed");
        Console.WriteLine();
    }

    private static void DemonstrateCoinChange()
    {
        Console.WriteLine("=== Coin change ===");

        // Greedy would take 4 + 1 + 1. The optimum is 3 + 3.
        var change = CoinChange.MakeChange(new[] { 1, 3, 4 }, 6);
        Expect(change.IsPossible && change.Count == 2, "fewest coins for 6");
        ExpectSequence("one optimal selection", change.Coins, 3, 3);

        var impossible = CoinChange.MakeChange(new[] { 2 }, 3);
        Expect(!impossible.IsPossible, "3 cannot be made from 2s");

        Expect(CoinChange.CountCombinations(new[] { 1, 2, 5 }, 5) == 4, "four combinations for 5");
        Console.WriteLine("  minimum coins and combination count checks passed");
        Console.WriteLine();
    }

    private static void DemonstrateLongestIncreasingSubsequence()
    {
        Console.WriteLine("=== Longest increasing subsequence ===");

        var sequence = LongestIncreasingSubsequence.Find(new[] { 10, 9, 2, 5, 3, 7, 101, 18 });
        ExpectSequence("one subsequence of length 4", sequence, 2, 3, 7, 18);

        var decreasing = LongestIncreasingSubsequence.Find(new[] { 3, 2, 1 });
        ExpectSequence("strictly decreasing input", decreasing, 1);
        Console.WriteLine();
    }

    private static void DemonstrateKnapsack()
    {
        Console.WriteLine("=== Knapsack ===");

        // Capacity 7. Items (1, 1), (3, 4), (4, 5), (5, 7).
        // Best value is 9, from the weight-3 item and the weight-4 item.
        var result = Knapsack.Solve(
            new[]
            {
                new Knapsack.Item(1, 1),
                new Knapsack.Item(3, 4),
                new Knapsack.Item(4, 5),
                new Knapsack.Item(5, 7)
            },
            capacity: 7);

        Expect(result.TotalValue == 9, "best value");
        ExpectSequence("chosen indexes", result.ChosenIndexes, 1, 2);
        Console.WriteLine();
    }

    private static void PrintTree(Tree<string>.Node? node, string indent = "    ")
    {
        if (node is null)
        {
            Console.WriteLine($"{indent}(empty)");
            return;
        }

        Console.WriteLine($"{indent}{node.Value}");
        foreach (var child in node.Children)
            PrintTree(child, indent + "  ");
    }

    private static List<T> Drain<T>(Heap<T> heap)
    {
        var values = new List<T>(heap.Count);
        while (heap.TryExtract(out var value))
            values.Add(value);
        return values;
    }

    private static List<TElement> Drain<TElement, TPriority>(DataStructures.PriorityQueue<TElement, TPriority> queue)
    {
        var values = new List<TElement>(queue.Count);
        while (queue.TryDequeue(out var value))
            values.Add(value!);
        return values;
    }

    private static void ExpectSequence<T>(string label, IEnumerable<T> actual, params T[] expected)
    {
        var items = actual.ToArray();
        if (!items.SequenceEqual(expected))
        {
            throw new InvalidOperationException(
                $"{label}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", items)}].");
        }

        Console.WriteLine($"  {label}: [{string.Join(", ", items)}]");
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException($"Check failed: {message}.");
    }

    private static void ExpectThrows<TException>(string label, Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            Console.WriteLine($"  {label}: threw {typeof(TException).Name}");
            return;
        }

        throw new InvalidOperationException($"{label}: expected {typeof(TException).Name}.");
    }

    /// <summary>
    /// Equality is normal string equality, but every hash code is 1.
    /// Used to force a single collision chain in the hash map demo.
    /// </summary>
    private sealed class SameHashComparer : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y) => StringComparer.Ordinal.Equals(x, y);

        public int GetHashCode(string obj) => 1;
    }
}
