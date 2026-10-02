namespace InterviewToolkit.DataStructures;

/*
 * General tree (n-ary)
 * --------------------
 * One root. Every other node has exactly one parent. A node may have any number
 * of children, including zero (a leaf).
 *
 *              A
 *            / | \
 *           B  C  D
 *          / \
 *         E   F
 *
 * A binary tree is the special case that allows at most two children. Values do
 * not have to be in sorted order: this type is about parent/child links, not
 * about searching by key. Duplicate values are allowed, so edit and delete work
 * on a node you already hold, not on "the" matching value.
 */

/// <summary>
/// A general tree of <typeparamref name="T"/> values. Each node has one parent
/// (except the root) and an ordered list of children.
/// </summary>
/// <typeparam name="T">The value stored in each node. May be a reference or a value type.</typeparam>
public sealed class Tree<T>
{
    /// <summary>
    /// One position in the tree. Create nodes through <see cref="AddRoot"/> and
    /// <see cref="AddChild"/> so the parent links and the tree's count stay correct.
    /// </summary>
    public sealed class Node
    {
        // The live child list. Callers see it only through the read-only view below,
        // so they cannot insert or remove children behind the tree's back.
        private readonly List<Node> _children = new();
        private readonly IReadOnlyList<Node> _childrenView;

        internal Node(Tree<T> tree, Node? parent, T value)
        {
            Tree = tree;
            Parent = parent;
            Value = value;
            IsAttached = true;
            _childrenView = _children.AsReadOnly();
        }

        /// <summary>The tree this node was created in. Stays set even after the node is deleted.</summary>
        public Tree<T> Tree { get; }

        /// <summary>The parent, or <see langword="null"/> when this node is the root.</summary>
        public Node? Parent { get; internal set; }

        /// <summary>The value stored at this node. Change it with <see cref="Tree{T}.Edit"/>.</summary>
        public T Value { get; internal set; }

        /// <summary>
        /// Children in insertion order, left to right. The list updates when children
        /// are added or removed; it cannot be edited directly.
        /// </summary>
        public IReadOnlyList<Node> Children => _childrenView;

        /// <summary>The root has no parent. A deleted node is not a root, even though its parent link is cleared.</summary>
        public bool IsRoot => IsAttached && Parent is null;

        /// <summary>A leaf has no children.</summary>
        public bool IsLeaf => _children.Count == 0;

        /// <summary>
        /// <see langword="false"/> after the node has been removed. Edit and delete
        /// refuse detached nodes so a stale reference cannot change the live tree.
        /// </summary>
        public bool IsAttached { get; internal set; }

        internal List<Node> ChildList => _children;
    }

    /// <summary>The top of the tree, or <see langword="null"/> when the tree is empty.</summary>
    public Node? Root { get; private set; }

    /// <summary>How many nodes are currently attached. Maintained by every mutating method, so this is O(1).</summary>
    public int Count { get; private set; }

    /// <summary><see langword="true"/> when <see cref="Count"/> is zero.</summary>
    public bool IsEmpty => Count == 0;

    /// <summary>
    /// Creates the root. A tree has only one root; call <see cref="Delete"/> or
    /// <see cref="Clear"/> before setting a new one.
    /// </summary>
    /// <returns>The new root node.</returns>
    public Node AddRoot(T value)
    {
        if (Root is not null)
        {
            throw new InvalidOperationException(
                "This tree already has a root. Delete it or call Clear before adding another.");
        }

        Root = new Node(this, parent: null, value);
        Count = 1;
        return Root;
    }

    /// <summary>
    /// Appends a child to <paramref name="parent"/>. O(1).
    /// </summary>
    /// <returns>The new child node. Hold onto it if you want to edit or delete that spot later.</returns>
    public Node AddChild(Node parent, T value)
    {
        EnsureAttached(parent);

        var child = new Node(this, parent, value);
        parent.ChildList.Add(child);
        Count++;
        return child;
    }

    /// <summary>
    /// Replaces the value stored at <paramref name="node"/>. The shape of the tree
    /// does not change. O(1).
    /// </summary>
    public void Edit(Node node, T newValue)
    {
        EnsureAttached(node);
        node.Value = newValue;
    }

    /// <summary>
    /// Finds the first node in preorder whose value equals <paramref name="currentValue"/>
    /// and replaces it. Returns <see langword="false"/> when no node matches.
    /// Values need not be unique, so this only changes the first hit. O(n).
    /// </summary>
    public bool EditFirst(T currentValue, T newValue)
    {
        var node = Find(currentValue);
        if (node is null)
            return false;

        node.Value = newValue;
        return true;
    }

    /// <summary>
    /// Removes <paramref name="node"/> and every node under it.
    /// The removed nodes stay linked to each other, but <see cref="Node.IsAttached"/>
    /// is cleared, so they can no longer be edited through this tree. O(subtree size).
    /// </summary>
    public void Delete(Node node)
    {
        EnsureAttached(node);
        UnlinkFromParent(node);
        Count -= MarkDetached(node);
    }

    /// <summary>
    /// Removes only <paramref name="node"/>. Its children move up to occupy the hole,
    /// in their existing left-to-right order.
    /// </summary>
    /// <remarks>
    /// <code>
    /// Before:          A                After:        A
    ///                /   \                          / | \
    ///               B     C                        D E  C
    ///              / \
    ///             D   E
    ///
    /// DeleteAndPromoteChildren(B) splices D and E in where B used to sit.
    /// </code>
    /// Promoting the root is only possible when it has zero children (the tree
    /// becomes empty) or one child (that child becomes the new root). Several
    /// children of the root would be a forest, and a tree has a single root.
    /// O(children of the parent).
    /// </remarks>
    public void DeleteAndPromoteChildren(Node node)
    {
        EnsureAttached(node);

        // Snapshot first. The child list is about to be rewritten, and a root with
        // several children must fail before any link changes.
        var children = node.ChildList.ToArray();
        if (node.Parent is null && children.Length > 1)
        {
            throw new InvalidOperationException(
                "Cannot promote the root's children: a tree has a single root, and the root has more than one child.");
        }

        if (node.Parent is null)
        {
            // Zero children: the tree is now empty. One child: that child becomes the root.
            Root = children.Length == 0 ? null : children[0];
            if (children.Length == 1)
                children[0].Parent = null;
        }
        else
        {
            var parent = node.Parent;
            int index = parent.ChildList.IndexOf(node);
            parent.ChildList.RemoveAt(index);
            // InsertRange keeps the children's order and leaves the following siblings in place.
            parent.ChildList.InsertRange(index, children);
            foreach (var child in children)
                child.Parent = parent;
        }

        node.ChildList.Clear();
        node.Parent = null;
        node.IsAttached = false;
        Count--;
    }

    /// <summary>
    /// Depth-first search in preorder (node, then each subtree left to right).
    /// Returns the first match, or <see langword="null"/>. O(n).
    /// </summary>
    public Node? Find(T value)
    {
        var equality = EqualityComparer<T>.Default;
        foreach (var node in TraversePreOrder())
        {
            if (equality.Equals(node.Value, value))
                return node;
        }

        return null;
    }

    /// <summary>
    /// Visits a node before its children. Reading the result left to right is a
    /// depth-first walk. O(n) time and extra space.
    /// </summary>
    public IReadOnlyList<Node> TraversePreOrder()
    {
        var result = new List<Node>(Count);
        CollectPreOrder(Root, result);
        return result;
    }

    /// <summary>
    /// Visits children before the node itself. A postorder walk deletes or frees
    /// a subtree from the leaves upward. O(n).
    /// </summary>
    public IReadOnlyList<Node> TraversePostOrder()
    {
        var result = new List<Node>(Count);
        CollectPostOrder(Root, result);
        return result;
    }

    /// <summary>
    /// Visits the root, then every node at depth 1, then depth 2, and so on.
    /// Implemented with a queue: dequeue a node, enqueue its children. O(n).
    /// </summary>
    public IReadOnlyList<Node> TraverseLevelOrder()
    {
        var result = new List<Node>(Count);
        if (Root is null)
            return result;

        var queue = new Queue<Node>();
        queue.Enqueue(Root);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            result.Add(current);
            foreach (var child in current.ChildList)
                queue.Enqueue(child);
        }

        return result;
    }

    /// <summary>Detaches every node and leaves the tree empty. O(n).</summary>
    public void Clear()
    {
        if (Root is not null)
            MarkDetached(Root);

        Root = null;
        Count = 0;
    }

    /// <summary>Pulls <paramref name="node"/> out of its parent's child list, or clears the root.</summary>
    private void UnlinkFromParent(Node node)
    {
        if (node.Parent is null)
        {
            Root = null;
            return;
        }

        node.Parent.ChildList.Remove(node);
        node.Parent = null;
    }

    /// <summary>
    /// Marks <paramref name="node"/> and every descendant as detached.
    /// The links inside the removed subtree are left intact so the caller can still
    /// inspect what was deleted. Returns how many nodes were marked.
    /// </summary>
    private static int MarkDetached(Node node)
    {
        int marked = 0;
        var stack = new Stack<Node>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            current.IsAttached = false;
            marked++;
            foreach (var child in current.ChildList)
                stack.Push(child);
        }

        return marked;
    }

    private static void CollectPreOrder(Node? node, List<Node> result)
    {
        if (node is null)
            return;

        result.Add(node);
        // Walk a snapshot of the child list so the walk is well-defined for this call.
        foreach (var child in node.ChildList)
            CollectPreOrder(child, result);
    }

    private static void CollectPostOrder(Node? node, List<Node> result)
    {
        if (node is null)
            return;

        foreach (var child in node.ChildList)
            CollectPostOrder(child, result);

        result.Add(node);
    }

    /// <summary>
    /// Rejects null, nodes from another tree, and nodes that were already removed.
    /// Every edit and delete starts here so the parent links stay consistent.
    /// </summary>
    private void EnsureAttached(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (!ReferenceEquals(node.Tree, this))
            throw new InvalidOperationException("The node belongs to a different tree.");

        if (!node.IsAttached)
            throw new InvalidOperationException("The node has already been removed from this tree.");
    }
}
