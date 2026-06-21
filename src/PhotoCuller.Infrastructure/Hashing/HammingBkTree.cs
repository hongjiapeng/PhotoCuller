namespace PhotoCuller.Infrastructure.Hashing;

internal sealed class HammingBkTree
{
    private readonly Func<ulong, ulong, int> _distance;
    private Node? _root;

    public HammingBkTree(Func<ulong, ulong, int> distance)
        => _distance = distance;

    public void Add(ulong hash, int index)
    {
        var node = new Node(hash, index);
        if (_root is null)
        {
            _root = node;
            return;
        }

        var current = _root;
        while (true)
        {
            var distance = _distance(hash, current.Hash);
            if (current.Children.TryGetValue(distance, out var child))
            {
                current = child;
                continue;
            }

            current.Children.Add(distance, node);
            return;
        }
    }

    public IEnumerable<(int Index, int Distance)> Find(ulong hash, int maxDistance)
    {
        if (_root is null)
            yield break;

        var stack = new Stack<Node>();
        stack.Push(_root);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            var distance = _distance(hash, node.Hash);
            if (distance <= maxDistance)
                yield return (node.Index, distance);

            var min = distance - maxDistance;
            var max = distance + maxDistance;
            foreach (var (edgeDistance, child) in node.Children)
            {
                if (edgeDistance >= min && edgeDistance <= max)
                    stack.Push(child);
            }
        }
    }

    private sealed class Node
    {
        public Node(ulong hash, int index)
        {
            Hash = hash;
            Index = index;
        }

        public ulong Hash { get; }
        public int Index { get; }
        public Dictionary<int, Node> Children { get; } = [];
    }
}
