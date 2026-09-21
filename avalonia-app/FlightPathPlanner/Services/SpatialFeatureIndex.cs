using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;

namespace FlightPathPlanner.Services;

/// <summary>An immutable spatial index over a dataset's feature bounding boxes (an STR-tree from NetTopologySuite, which the
/// app already uses). Map viewport queries return only the features that can be on screen; when more than the caller can draw
/// qualify, the largest ones (by weight, i.e. area) win, so a zoomed-out view shows the significant shapes.
/// Built once at import; safe for concurrent queries afterwards.</summary>
public sealed class SpatialFeatureIndex
{
    private readonly STRtree<int> _tree = new(10);
    private readonly Envelope[] _bounds;
    private readonly double[] _weights;

    public SpatialFeatureIndex(IReadOnlyList<Envelope> bounds, IReadOnlyList<double> weights)
    {
        _bounds = bounds.ToArray();
        _weights = weights.ToArray();
        for (int i = 0; i < _bounds.Length; i++)
        {
            if (!_bounds[i].IsNull) _tree.Insert(_bounds[i], i);
        }
        _tree.Build(); // build eagerly: Build() is not thread-safe, queries after it are
    }

    public int Count => _bounds.Length;

    public Envelope BoundsOf(int index) => _bounds[index];

    /// <summary>Union of the bounds of every feature the mask includes (null mask = all).</summary>
    public Envelope BoundsOfIncluded(bool[]? mask)
    {
        var total = new Envelope();
        for (int i = 0; i < _bounds.Length; i++)
        {
            if ((mask == null || mask[i]) && !_bounds[i].IsNull) total.ExpandToInclude(_bounds[i]);
        }
        return total;
    }

    /// <param name="viewport">Area on screen (null = everywhere).</param>
    /// <param name="mask">Which features are currently included by filters (null = all).</param>
    /// <param name="maxResults">Cap on returned features.</param>
    /// <returns>Indexes of features to draw, and how many qualified before the cap.</returns>
    public (List<int> Indexes, int Candidates) Query(Envelope? viewport, bool[]? mask, int maxResults)
    {
        IEnumerable<int> hits = viewport == null || viewport.IsNull
            ? Enumerable.Range(0, _bounds.Length).Where(i => !_bounds[i].IsNull)
            : _tree.Query(viewport);

        var list = mask == null ? hits.ToList() : hits.Where(i => mask[i]).ToList();
        int candidates = list.Count;

        if (candidates > maxResults)
        {
            list.Sort((a, b) => _weights[b].CompareTo(_weights[a]));
            list.RemoveRange(maxResults, candidates - maxResults);
            list.Sort(); // stable draw order
        }
        else
        {
            list.Sort();
        }
        return (list, candidates);
    }
}
