namespace Adventures.Data.NQuad;

/// <summary>
/// A bounded least-recently-used cache of dictionary terms, both ways (text to id, id to text), shared by the readers and writers of one store instance. Bounded so a shared hosting pool
/// never holds the whole dictionary (the benchmark counted about half a million IRI terms for 1,000 blogs): the predicates, types and graphs that every query touches stay hot, a subject
/// that is not touched for a while is dropped. Thread-safe; every operation is O(1).
/// </summary>
public sealed class TermCache(int capacity)
{
    private readonly int _capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    private readonly Dictionary<string, LinkedListNode<(string Value, long Id)>> _byValue = new(StringComparer.Ordinal);
    private readonly Dictionary<long, LinkedListNode<(string Value, long Id)>> _byId = [];
    private readonly LinkedList<(string Value, long Id)> _recent = new();
    private readonly object _gate = new();

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _byValue.Count;
            }
        }
    }

    public bool TryGetId(string value, out long id)
    {
        lock (_gate)
        {
            if (_byValue.TryGetValue(value, out var node))
            {
                Touch(node);
                id = node.Value.Id;
                return true;
            }
        }

        id = 0;
        return false;
    }

    public bool TryGetValue(long id, out string value)
    {
        lock (_gate)
        {
            if (_byId.TryGetValue(id, out var node))
            {
                Touch(node);
                value = node.Value.Value;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    public void Add(string value, long id)
    {
        lock (_gate)
        {
            if (_byValue.TryGetValue(value, out var existing))
            {
                Touch(existing);
                return;
            }

            var node = _recent.AddFirst((value, id));
            _byValue[value] = node;
            _byId[id] = node;
            while (_byValue.Count > _capacity)
            {
                var oldest = _recent.Last!;
                _recent.RemoveLast();
                _byValue.Remove(oldest.Value.Value);
                _byId.Remove(oldest.Value.Id);
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _byValue.Clear();
            _byId.Clear();
            _recent.Clear();
        }
    }

    private void Touch(LinkedListNode<(string Value, long Id)> node)
    {
        if (node != _recent.First)
        {
            _recent.Remove(node);
            _recent.AddFirst(node);
        }
    }
}
