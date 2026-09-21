using System.Globalization;
using System.Text;

namespace FlightPathPlanner.Tests.TestData;

/// <summary>Synthetic OPS files in the real operation-plan schema (operationPlanId, state, closureReason, publicInfo, operationVolumes[]
/// with operationGeometry.geom). Deterministic for a seed.</summary>
public static class OpsDatasetGenerator
{
    private static readonly string[] Reasons = { "NOMINAL", "WITHDRAWN", "TIMEOUT", "EMERGENCY" };

    private static string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    public static string Generate(int count, int seed = 42, bool wrapped = false, int malformedEvery = 0)
    {
        var sb = new StringBuilder(count * 700);
        sb.Append(wrapped ? "{\"plans\":[" : "[");
        for (int i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(',');
            var rng = new Random(seed * 7919 + i);
            if (malformedEvery > 0 && i % malformedEvery == malformedEvery - 1)
            {
                sb.Append("\"not an operation\"");
                continue;
            }

            double cx = 20 + rng.NextDouble() * 10, cy = 50 + rng.NextDouble() * 8;
            int day = 1 + i % 27, month = 1 + i % 12;
            sb.Append("{\"operationPlanId\":\"OP-").Append(i.ToString("D6")).Append("\",\"state\":\"CLOSED\",\"operator\":\"OPERATOR").Append(i % 40)
              .Append("\",\"closureReason\":\"").Append(Reasons[i % Reasons.Length])
              .Append("\",\"publicInfo\":{\"title\":\"Flight ").Append(i).Append(i % 5 == 0 ? " survey" : " delivery")
              .Append("\",\"description\":\"Generated plan ").Append(i).Append("\"},\"operationVolumes\":[");
            int volumes = 1 + i % 3;
            for (int v = 0; v < volumes; v++)
            {
                if (v > 0) sb.Append(',');
                double x = cx + v * 0.01, y = cy + v * 0.01, d = 0.005 + rng.NextDouble() * 0.01;
                sb.Append("{\"timeBegin\":\"2025-").Append(month.ToString("D2")).Append('-').Append(day.ToString("D2")).Append("T08:00:00.000Z\",\"timeEnd\":\"2025-")
                  .Append(month.ToString("D2")).Append('-').Append(day.ToString("D2")).Append("T09:30:00.000Z\",\"ordinal\":").Append(v)
                  .Append(",\"operationGeometry\":{\"minAltitude\":{\"altitudeValue\":0,\"unitsOfMeasure\":\"FT\"},\"maxAltitude\":{\"altitudeValue\":")
                  .Append(200 + i % 200).Append(",\"unitsOfMeasure\":\"FT\"},\"geom\":{\"type\":\"Polygon\",\"coordinates\":[[[")
                  .Append(F(x)).Append(',').Append(F(y)).Append("],[").Append(F(x + d)).Append(',').Append(F(y)).Append("],[")
                  .Append(F(x + d)).Append(',').Append(F(y + d)).Append("],[").Append(F(x)).Append(',').Append(F(y + d)).Append("],[")
                  .Append(F(x)).Append(',').Append(F(y)).Append("]]]}}}");
            }
            sb.Append("]}");
        }
        sb.Append(wrapped ? "]}" : "]");
        return sb.ToString();
    }
}
