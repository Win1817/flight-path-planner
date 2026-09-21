using System.Diagnostics;
using FlightPathPlanner.Models;
using FlightPathPlanner.Services.Import;
using NetTopologySuite.Geometries;

namespace FlightPathPlanner.Services;

/// <summary>One update for the map page: the features to draw (as JSON chunks), whether to move the camera, and how much of the
/// dataset those features represent.</summary>
public sealed record MapPayload(
    int Version,
    bool ViewportMode,
    bool Fit,
    double[]? FitBounds,
    int Shown,
    int Total,
    bool AnyHighlight,
    IReadOnlyList<string> Chunks,
    TimeSpan BuildTime,
    int InView = 0,
    bool NeedsViewport = false)
{
    public static MapPayload Empty(int version, bool fit) => new(version, false, fit, null, 0, 0, false, Array.Empty<string>(), TimeSpan.Zero);
}

/// <summary>Decides WHICH features the map gets, from immutable dataset snapshots, so it can run on a worker thread.
/// Small sets are sent whole. Big sets are sent by viewport: only the features whose bounds intersect the visible area (via the
/// spatial index), capped so the page never has to draw the whole dataset; when more qualify than fit, the largest are drawn and
/// the page shows a "zoom in for more" note.</summary>
public static class MapPayloadBuilder
{
    /// <summary>Feature sets above this size switch to viewport rendering.</summary>
    public const int ViewportThreshold = 1500;

    /// <summary>Most features sent for one viewport.</summary>
    public const int MaxViewportFeatures = 2500;

    private const double ViewportPadding = 0.15; // query a little beyond the screen so small pans don't reveal empty edges

    public static bool UsesViewport(int includedCount) => includedCount > ViewportThreshold;

    public static Envelope Expand(Envelope e, double fraction)
    {
        var copy = new Envelope(e);
        copy.ExpandBy(e.Width * fraction, e.Height * fraction);
        return copy;
    }

    private static double[]? ToArray(Envelope? e) =>
        e == null || e.IsNull ? null : new[] { e.MinX, e.MinY, e.MaxX, e.MaxY };

    public static MapPayload ForAors(AorDataset dataset, bool[]? mask, int included, IReadOnlySet<string> highlightIds,
        Envelope? viewport, bool fit, int version, CancellationToken ct)
    {
        var timer = Stopwatch.StartNew();
        var fitBounds = fit ? dataset.Index.BoundsOfIncluded(mask) : null;

        if (!UsesViewport(included))
        {
            var items = mask == null ? dataset.Items : dataset.Items.Where((_, i) => mask[i]).ToArray();
            var chunks = MapGeoJson.BuildChunks(Array.Empty<ParsedOps>(), items, null, highlightIds, ct: ct);
            return new MapPayload(version, false, fit, ToArray(fitBounds), items.Length, items.Length, highlightIds.Count > 0, chunks, timer.Elapsed);
        }

        var view = FitOrViewport(viewport, fitBounds, fit);
        if (view == null) return new MapPayload(version, true, fit, ToArray(fitBounds), 0, included, highlightIds.Count > 0, Array.Empty<string>(), timer.Elapsed, 0, NeedsViewport: true);

        var (indexes, candidates) = dataset.Index.Query(Expand(view, ViewportPadding), mask, MaxViewportFeatures);
        var visible = indexes.Select(i => dataset.Items[i]);
        var visibleChunks = MapGeoJson.BuildChunks(Array.Empty<ParsedOps>(), visible, null, highlightIds, ct: ct);
        return new MapPayload(version, true, fit, ToArray(fitBounds), indexes.Count, included, highlightIds.Count > 0, visibleChunks, timer.Elapsed, candidates);
    }

    public static MapPayload ForOps(OpsDataset dataset, bool[]? mask, int included, IReadOnlySet<string> highlightIds,
        Envelope? viewport, bool fit, int version, CancellationToken ct)
    {
        var timer = Stopwatch.StartNew();
        var fitBounds = fit ? dataset.Index.BoundsOfIncluded(mask) : null;

        if (!UsesViewport(included))
        {
            var items = mask == null ? dataset.Items : dataset.Items.Where((_, i) => mask[i]).ToArray();
            var chunks = MapGeoJson.BuildChunks(items, Array.Empty<ParsedAor>(), null, highlightIds, ct: ct);
            return new MapPayload(version, false, fit, ToArray(fitBounds), items.Length, items.Length, highlightIds.Count > 0, chunks, timer.Elapsed);
        }

        var view = FitOrViewport(viewport, fitBounds, fit);
        if (view == null) return new MapPayload(version, true, fit, ToArray(fitBounds), 0, included, highlightIds.Count > 0, Array.Empty<string>(), timer.Elapsed, 0, NeedsViewport: true);

        var (indexes, candidates) = dataset.Index.Query(Expand(view, ViewportPadding), mask, MaxViewportFeatures);
        var visible = indexes.Select(i => dataset.Items[i]);
        var visibleChunks = MapGeoJson.BuildChunks(visible, Array.Empty<ParsedAor>(), null, highlightIds, ct: ct);
        return new MapPayload(version, true, fit, ToArray(fitBounds), indexes.Count, included, highlightIds.Count > 0, visibleChunks, timer.Elapsed, candidates);
    }

    /// <summary>Report / lookup: a handful of shapes, always sent whole.</summary>
    public static MapPayload ForSmallSet(IReadOnlyList<ParsedOps> ops, IReadOnlyList<ParsedAor> aors, (double lon, double lat, double radiusKm)? radius,
        IReadOnlySet<string> highlightIds, bool fit, int version, CancellationToken ct)
    {
        var timer = Stopwatch.StartNew();
        var chunks = MapGeoJson.BuildChunks(ops, aors, radius, highlightIds, ct: ct);

        Envelope? bounds = null;
        if (fit)
        {
            bounds = new Envelope();
            foreach (var op in ops) bounds.ExpandToInclude(op.Bounds);
            foreach (var aor in aors) bounds.ExpandToInclude(aor.Bounds);
            if (radius is { } r)
            {
                var circle = new Models.Geometry { Type = "Polygon", Polygons = new[] { GeoMath.Circle(r.lon, r.lat, r.radiusKm) } };
                bounds.ExpandToInclude(circle.ComputeBounds());
            }
        }
        int count = ops.Count + aors.Count + (radius.HasValue ? 1 : 0);
        return new MapPayload(version, false, fit, ToArray(bounds), count, count, highlightIds.Count > 0, chunks, timer.Elapsed);
    }

    // When the camera is about to move (fit), what will be on screen is the target bounds — query that so features arrive with the
    // move instead of waiting for the page to report its new viewport. Otherwise use the last reported viewport.
    private static Envelope? FitOrViewport(Envelope? viewport, Envelope? fitBounds, bool fit) =>
        fit && fitBounds is { IsNull: false } ? fitBounds : viewport;
}
