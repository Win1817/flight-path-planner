using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FlightPathPlanner.Models;

namespace FlightPathPlanner.Services;

/// <summary>Ported from src/utils/opsUtils.ts — keep in sync with that file's parsing rules.</summary>
public static partial class OpsParser
{
    public static readonly string[] ZoneColors =
    {
        "#3B82F6", "#10B981", "#F59E0B", "#EF4444", "#8B5CF6",
        "#EC4899", "#06B6D4", "#F97316", "#22C55E", "#6366F1",
    };

    [GeneratedRegex(@"(Z|[+-]\d{2}:?\d{2})$")]
    private static partial Regex TimezoneSuffixRegex();

    /// <summary>
    /// If a timestamp has no timezone marker, assume UTC and append "Z".
    /// This exact regex fixed a real bug in the web app (an earlier version required a digit
    /// after the Z/+/- marker, which meant a bare trailing "Z" never matched, silently
    /// corrupting every UTC timestamp into "...Z" -> "...ZZ" -> an unparseable date). Do not
    /// "simplify" this back to the broken version.
    /// </summary>
    public static string? EnsureUtc(string? time)
    {
        if (string.IsNullOrEmpty(time)) return null;
        return TimezoneSuffixRegex().IsMatch(time) ? time : time + "Z";
    }

    public static List<Ops> ParseOps(JsonElement data)
    {
        var rawOps = new List<JsonElement>();

        if (data.ValueKind == JsonValueKind.Array)
        {
            rawOps.AddRange(data.EnumerateArray());
        }
        else if (data.ValueKind == JsonValueKind.Object)
        {
            if (IsSingleOpsRecord(data))
            {
                rawOps.Add(data);
            }
            else if (JsonHelpers.GetArray(data, "plans") is { } plans) rawOps.AddRange(plans.EnumerateArray());
            else if (JsonHelpers.GetArray(data, "operations") is { } ops) rawOps.AddRange(ops.EnumerateArray());
            else if (JsonHelpers.GetArray(data, "flight_plans") is { } fp) rawOps.AddRange(fp.EnumerateArray());
        }

        if (rawOps.Count == 0)
            throw new InvalidDataException("Invalid OPS data format");

        return rawOps.Select(NormalizeOps).ToList();
    }

    /// <summary>True for a single operation-plan object — either the ED-269 shape (plan fields at the top level) or the
    /// ED-318 shape (plan fields nested one level deeper, under "operationPlan") — as opposed to a wrapper holding an
    /// array of them.</summary>
    public static bool IsSingleOpsRecord(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object) return false;
        if (data.TryGetProperty("operation_plan_id", out _) || data.TryGetProperty("operationPlanId", out _) ||
            data.TryGetProperty("operation_volumes", out _) || data.TryGetProperty("operationVolumes", out _))
            return true;
        return JsonHelpers.GetObject(data, "operationPlan") is { } inner && IsSingleOpsRecord(inner);
    }

    /// <summary>ED-318 nests every plan field one level deeper, under "operationPlan" (alongside provider/approval
    /// metadata this app doesn't use); ED-269 has them at the top level. Unwrapping once here means every field rule
    /// below works for both schemas without duplicating them.</summary>
    private static JsonElement UnwrapOperationPlan(JsonElement raw) =>
        JsonHelpers.GetObject(raw, "operationPlan") ?? raw;

    public static Ops NormalizeOps(JsonElement raw)
    {
        var data = UnwrapOperationPlan(raw);

        var operationPlanId = JsonHelpers.GetString(data, "operationPlanId", "operation_plan_id") ?? "";
        var flightPlanId = JsonHelpers.GetString(data, "flightPlanId", "flight_plan_id");

        var publicInfo = JsonHelpers.GetObject(data, "publicInfo");
        var title = JsonHelpers.GetString(data, "title")
            ?? (publicInfo.HasValue ? JsonHelpers.GetString(publicInfo.Value, "title") : null)
            ?? "";

        var flightDetails = JsonHelpers.GetObject(data, "flightDetails");
        var description = JsonHelpers.GetString(data, "description")
            ?? (publicInfo.HasValue ? JsonHelpers.GetString(publicInfo.Value, "description") : null)
            ?? (flightDetails.HasValue ? JsonHelpers.GetString(flightDetails.Value, "flightComment") : null)
            ?? "";

        var rawVolumes = JsonHelpers.GetArray(data, "operationVolumes", "operation_volumes");
        var volumes = rawVolumes.HasValue
            ? rawVolumes.Value.EnumerateArray().Select(NormalizeVolume).ToList()
            : new List<OperationVolume>();

        var rawContact = JsonHelpers.GetObject(data, "contactDetails", "contact");
        Contact? contact = null;
        if (rawContact.HasValue)
        {
            var c = rawContact.Value;
            var firstName = JsonHelpers.GetString(c, "firstName") ?? "";
            var lastName = JsonHelpers.GetString(c, "lastName") ?? "";
            var name = JsonHelpers.GetString(c, "name") ?? $"{firstName} {lastName}".Trim();
            var phones = JsonHelpers.GetArray(c, "phones");
            var emails = JsonHelpers.GetArray(c, "emails");
            var phone = JsonHelpers.GetString(c, "phone")
                ?? (phones.HasValue && phones.Value.GetArrayLength() > 0 ? phones.Value[0].GetString() : null);
            var email = JsonHelpers.GetString(c, "email")
                ?? (emails.HasValue && emails.Value.GetArrayLength() > 0 ? emails.Value[0].GetString() : null);
            contact = new Contact { Name = name, Phone = phone, Email = email };
        }

        return new Ops
        {
            OperationPlanId = operationPlanId,
            FlightPlanId = flightPlanId,
            Operator = JsonHelpers.GetString(data, "operator"),
            Title = title,
            Description = description,
            State = JsonHelpers.GetString(data, "state"),
            ClosureReason = JsonHelpers.GetString(data, "closureReason"),
            SubmitTime = EnsureUtc(JsonHelpers.GetString(data, "submitTime", "submit_time")),
            UpdateTime = EnsureUtc(JsonHelpers.GetString(data, "updateTime", "update_time")),
            OperationVolumes = volumes,
            Contact = contact,
            ModeOfOperation = JsonHelpers.GetString(data, "modeOfOperation"),
            SwarmSize = JsonHelpers.GetNumber(data, "swarmSize"),
        };
    }

    private static OperationVolume NormalizeVolume(JsonElement vol)
    {
        var timeBegin = EnsureUtc(JsonHelpers.GetString(vol, "timeBegin", "effective_time_begin") ?? "") ?? "";
        var timeEnd = EnsureUtc(JsonHelpers.GetString(vol, "timeEnd", "effective_time_end") ?? "") ?? "";
        var actualTimeEnd = EnsureUtc(JsonHelpers.GetString(vol, "actualTimeEnd", "actual_time_end"));

        var operationGeometry = JsonHelpers.GetObject(vol, "operationGeometry");
        var directGeography = JsonHelpers.GetObject(vol, "operation_geography");

        Geometry? geography = null;
        if (operationGeometry.HasValue &&
            operationGeometry.Value.TryGetProperty("geom", out var geom) &&
            geom.ValueKind == JsonValueKind.Object)
        {
            geography = GeometryParser.Parse(geom);
        }
        else if (directGeography.HasValue)
        {
            geography = GeometryParser.Parse(directGeography.Value);
        }

        var minAltRaw = (operationGeometry.HasValue ? JsonHelpers.GetObject(operationGeometry.Value, "minAltitude") : null)
            ?? JsonHelpers.GetObject(vol, "min_altitude");
        var maxAltRaw = (operationGeometry.HasValue ? JsonHelpers.GetObject(operationGeometry.Value, "maxAltitude") : null)
            ?? JsonHelpers.GetObject(vol, "max_altitude");

        var ordinal = JsonHelpers.GetNumber(vol, "ordinal");

        return new OperationVolume
        {
            Id = JsonHelpers.GetString(vol, "id", "alias"),
            Ordinal = ordinal.HasValue ? (int)ordinal.Value : null,
            EffectiveTimeBegin = timeBegin,
            EffectiveTimeEnd = timeEnd,
            ActualTimeEnd = actualTimeEnd,
            MinAltitude = ParseAltitude(minAltRaw),
            MaxAltitude = ParseAltitude(maxAltRaw),
            OperationGeography = geography,
            BeyondVisualLineOfSight = JsonHelpers.GetBool(vol, "isBVLOS", "beyond_visual_line_of_sight"),
        };
    }

    private static Altitude? ParseAltitude(JsonElement? altRaw)
    {
        if (!altRaw.HasValue) return null;
        var a = altRaw.Value;
        return new Altitude
        {
            AltitudeValue = JsonHelpers.GetNumber(a, "altitudeValue", "altitude_value") ?? 0,
            UnitsOfMeasure = JsonHelpers.GetString(a, "unitsOfMeasure", "units_of_measure") ?? "FT",
            VerticalReference = JsonHelpers.GetString(a, "altitudeType", "vertical_reference"),
        };
    }

    public static ParsedOps ProcessOps(Ops op, int index)
    {
        var allVolumes = op.OperationVolumes.Concat(op.OffNominalVolumes).ToList();
        var (start, end) = GetVolumeTimeRange(allVolumes);

        double totalArea = 0;
        var bounds = new NetTopologySuite.Geometries.Envelope();
        int zoneCount = 0;
        foreach (var v in allVolumes)
        {
            if (v.OperationGeography == null) continue;
            totalArea += v.OperationGeography.ComputeArea();
            bounds.ExpandToInclude(v.OperationGeography.ComputeBounds());
            zoneCount++;
        }

        return new ParsedOps
        {
            OperationPlanId = op.OperationPlanId,
            FlightPlanId = op.FlightPlanId,
            Operator = op.Operator,
            Title = op.Title,
            Description = op.Description,
            State = op.State,
            ClosureReason = op.ClosureReason,
            SubmitTime = op.SubmitTime,
            UpdateTime = op.UpdateTime,
            OperationVolumes = op.OperationVolumes,
            OffNominalVolumes = op.OffNominalVolumes,
            Contact = op.Contact,
            ModeOfOperation = op.ModeOfOperation,
            SwarmSize = op.SwarmSize,
            ComputedArea = totalArea,
            StartTime = start,
            EndTime = end,
            ZoneCount = zoneCount,
            Color = ZoneColors[index % ZoneColors.Length],
            Ordinal = index,
            Bounds = bounds,
            SearchText = string.Join('\u0001', op.Title ?? "", op.OperationPlanId, op.Operator ?? "", op.Description ?? "").ToLowerInvariant(),
        };
    }

    /// <summary>
    /// Min/max of all volumes' begin/end times; falls back to "now" if none parse (matches the
    /// web app's fallback — callers should never feed this unparseable timestamps in practice,
    /// since EnsureUtc already fixes the common cause of that).
    /// </summary>
    private static (DateTimeOffset start, DateTimeOffset end) GetVolumeTimeRange(List<OperationVolume> volumes)
    {
        var times = new List<DateTimeOffset>();
        foreach (var v in volumes)
        {
            if (DateTimeOffset.TryParse(v.EffectiveTimeBegin, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var begin))
                times.Add(begin);
            if (DateTimeOffset.TryParse(v.EffectiveTimeEnd, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var end))
                times.Add(end);
        }

        if (times.Count == 0)
        {
            var now = DateTimeOffset.UtcNow;
            return (now, now);
        }

        return (times.Min(), times.Max());
    }

    public static string GetOperationStatus(DateTimeOffset startTime, DateTimeOffset endTime)
    {
        var now = DateTimeOffset.UtcNow;
        if (now < startTime) return "pending";
        if (now > endTime) return "expired";
        return "active";
    }

    public static string FormatArea(double squareMeters)
    {
        if (squareMeters >= 1_000_000) return $"{squareMeters / 1_000_000:F2} km²";
        if (squareMeters >= 10_000) return $"{squareMeters / 10_000:F2} ha";
        return $"{squareMeters:F0} m²";
    }

    public static string FormatDateTime(DateTimeOffset date) =>
        date.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";

    public static string FormatDateTimeShort(DateTimeOffset date) =>
        date.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Min start / max end across all ops, used to auto-populate the timeframe filter on load.</summary>
    public static (DateTimeOffset from, DateTimeOffset to)? GetOverallTimeRange(IReadOnlyList<ParsedOps> ops)
    {
        if (ops.Count == 0) return null;
        return (ops.Min(o => o.StartTime), ops.Max(o => o.EndTime));
    }
}
