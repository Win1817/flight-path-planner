using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FlightPathPlanner.Services;

public sealed record GeocodeResult(double Lat, double Lng, string DisplayName);

/// <summary>Ported from src/utils/geocodeUtils.ts — keep in sync with that file's parsing/lookup rules.</summary>
public static partial class GeocodeService
{
    // Matches "lat,long", "lat:long", or "lat long" (with optional decimals/negatives).
    [GeneratedRegex(@"^\s*(-?\d+(?:\.\d+)?)\s*[,:\s]\s*(-?\d+(?:\.\d+)?)\s*$")]
    private static partial Regex CoordPattern();

    public static GeocodeResult? ParseCoordinates(string query)
    {
        var match = CoordPattern().Match(query);
        if (!match.Success) return null;

        if (!double.TryParse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out var lat)) return null;
        if (!double.TryParse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture, out var lng)) return null;
        if (lat < -90 || lat > 90 || lng < -180 || lng > 180) return null;

        return new GeocodeResult(lat, lng, $"{lat:F5}, {lng:F5}");
    }

    private sealed record NominatimResult(
        [property: JsonPropertyName("lat")] string Lat,
        [property: JsonPropertyName("lon")] string Lon,
        [property: JsonPropertyName("display_name")] string DisplayName);

    private static async Task<List<GeocodeResult>> GeocodeAddressAsync(HttpClient httpClient, string query, CancellationToken ct)
    {
        var url = $"https://nominatim.openstreetmap.org/search?format=json&limit=5&q={Uri.EscapeDataString(query)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("User-Agent", "FlightPathPlanner/1.0");

        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("Geocoding request failed. Please try again.");

        var data = await response.Content.ReadFromJsonAsync<List<NominatimResult>>(cancellationToken: ct) ?? new();
        return data.Select(item => new GeocodeResult(
            double.Parse(item.Lat, System.Globalization.CultureInfo.InvariantCulture),
            double.Parse(item.Lon, System.Globalization.CultureInfo.InvariantCulture),
            item.DisplayName)).ToList();
    }

    /// <summary>Resolves a search query to one or more candidate locations: parses it as raw
    /// coordinates first, falling back to address geocoding via Nominatim.</summary>
    public static async Task<List<GeocodeResult>> ResolveLocationAsync(HttpClient httpClient, string query, CancellationToken ct = default)
    {
        var direct = ParseCoordinates(query);
        if (direct != null) return new List<GeocodeResult> { direct };
        return await GeocodeAddressAsync(httpClient, query, ct);
    }
}
