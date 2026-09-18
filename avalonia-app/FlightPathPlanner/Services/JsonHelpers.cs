using System.Text.Json;

namespace FlightPathPlanner.Services;

/// <summary>
/// Duck-typed field lookup over JsonElement, mirroring the TypeScript parsers' pattern of
/// `raw.fooCamel || raw.foo_snake` across multiple possible source-field-name conventions.
/// </summary>
internal static class JsonHelpers
{
    public static string? GetString(JsonElement obj, params string[] names)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in names)
        {
            if (obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
            {
                var s = v.GetString();
                if (!string.IsNullOrEmpty(s)) return s;
            }
        }
        return null;
    }

    public static double? GetNumber(JsonElement obj, params string[] names)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in names)
        {
            if (obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number)
            {
                return v.GetDouble();
            }
        }
        return null;
    }

    public static bool? GetBool(JsonElement obj, params string[] names)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in names)
        {
            if (obj.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False))
            {
                return v.GetBoolean();
            }
        }
        return null;
    }

    public static JsonElement? GetObject(JsonElement obj, params string[] names)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in names)
        {
            if (obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object)
            {
                return v;
            }
        }
        return null;
    }

    public static JsonElement? GetArray(JsonElement obj, params string[] names)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in names)
        {
            if (obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array)
            {
                return v;
            }
        }
        return null;
    }

    public static List<string>? GetStringArray(JsonElement obj, params string[] names)
    {
        var arr = GetArray(obj, names);
        if (!arr.HasValue) return null;
        return arr.Value.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .ToList();
    }
}
