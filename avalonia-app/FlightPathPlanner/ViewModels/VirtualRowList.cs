using System.Collections;
using System.Collections.Specialized;

namespace FlightPathPlanner.ViewModels;

/// <summary>A read-only list of row view models that creates each row only when the UI asks for it. A dataset of 25,000 items has
/// 25,000 entries here but only the handful of rows scrolled into view ever exist, so importing or filtering never allocates a
/// view model (or a visual) per item. Rows are keyed by the item's position in the dataset; a filter change is one Reset.</summary>
public sealed class VirtualRowList<TRow> : IReadOnlyList<TRow>, IList, INotifyCollectionChanged where TRow : class
{
    private readonly Func<int, TRow> _createRow;
    private readonly Dictionary<int, TRow> _rows = new();
    private int[] _positions = Array.Empty<int>();

    public VirtualRowList(Func<int, TRow> createRow) => _createRow = createRow;

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    /// <summary>Dataset positions currently shown, ascending.</summary>
    public IReadOnlyList<int> Positions => _positions;

    /// <summary>Rows that have been materialised so far (for pushing state such as "select all" into visible rows).</summary>
    public IEnumerable<TRow> MaterialisedRows => _rows.Values;

    public void Reset(int[] positions)
    {
        _positions = positions;
        _rows.Clear();
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public int Count => _positions.Length;

    public TRow this[int index]
    {
        get
        {
            int position = _positions[index];
            if (!_rows.TryGetValue(position, out var row)) _rows[position] = row = _createRow(position);
            return row;
        }
    }

    /// <summary>Row for a dataset position that is currently shown, or null.</summary>
    public TRow? RowAtPosition(int position)
    {
        int index = Array.BinarySearch(_positions, position);
        return index >= 0 ? this[index] : null;
    }

    public IEnumerator<TRow> GetEnumerator()
    {
        // Enumerating materialises every row — fine for tests and small lists, never used on the import/map paths.
        for (int i = 0; i < Count; i++) yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // ---- IList (non-generic) so the items control indexes into the list instead of enumerating it ----
    object? IList.this[int index] { get => this[index]; set => throw new NotSupportedException(); }
    bool IList.IsFixedSize => true;
    bool IList.IsReadOnly => true;
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => this;
    int IList.Add(object? value) => throw new NotSupportedException();
    void IList.Clear() => throw new NotSupportedException();
    void IList.Insert(int index, object? value) => throw new NotSupportedException();
    void IList.Remove(object? value) => throw new NotSupportedException();
    void IList.RemoveAt(int index) => throw new NotSupportedException();
    bool IList.Contains(object? value) => ((IList)this).IndexOf(value) >= 0;
    void ICollection.CopyTo(Array array, int index) { foreach (var row in this) array.SetValue(row, index++); }

    int IList.IndexOf(object? value)
    {
        if (value is not TRow row) return -1;
        foreach (var (position, candidate) in _rows)
        {
            if (!ReferenceEquals(candidate, row)) continue;
            int index = Array.BinarySearch(_positions, position);
            return index >= 0 ? index : -1;
        }
        return -1;
    }
}
