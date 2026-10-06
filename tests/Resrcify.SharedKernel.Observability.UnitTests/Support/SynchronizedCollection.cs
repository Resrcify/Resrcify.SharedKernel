using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace Resrcify.SharedKernel.Observability.UnitTests.Support;

/// <summary>A collection an exporter can add to on the server's threads while a test reads it.</summary>
internal sealed class SynchronizedCollection<T>
    : ICollection<T>
{
    private readonly List<T> _items = [];
    private readonly Lock _lock = new();

    public int Count
    {
        get
        {
            lock (_lock)
                return _items.Count;
        }
    }

    public bool IsReadOnly => false;

    public void Add(T item)
    {
        lock (_lock)
            _items.Add(item);
    }

    public void Clear()
    {
        lock (_lock)
            _items.Clear();
    }

    public bool Contains(T item)
    {
        lock (_lock)
            return _items.Contains(item);
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        lock (_lock)
            _items.CopyTo(array, arrayIndex);
    }

    public bool Remove(T item)
    {
        lock (_lock)
            return _items.Remove(item);
    }

    public IEnumerator<T> GetEnumerator()
    {
        lock (_lock)
            return new List<T>(_items).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();
}
