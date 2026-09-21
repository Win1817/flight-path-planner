using System.Text.Json;

namespace FlightPathPlanner.Services.Import;

/// <summary>Streams the records of an OPS/AoR file one at a time, understanding the same three layouts the DOM parsers accept:
/// a top-level array, an object wrapping an array under a known property name, or a single record object.</summary>
internal static class RecordStreamer
{
    public sealed record Result(bool Handled, TimeSpan ReadTime);

    public static async Task<Result> StreamAsync(
        ImportSource source,
        string[] wrapperNames,
        Func<JsonElement, bool> looksLikeSingleRecord,
        Action<JsonElement> onRecord,
        Action<long> onBytesConsumed,
        CancellationToken ct)
    {
        bool handled = false;
        bool topLevelObject = false;
        TimeSpan readTime;

        using (var reader = new JsonStreamReader(source.Open()))
        {
            if (!await reader.ReadAsync(ct).ConfigureAwait(false)) throw new InvalidDataException("The file is empty.");

            if (reader.TokenType == JsonTokenType.StartArray)
            {
                await StreamElementsAsync(reader, onRecord, onBytesConsumed, ct).ConfigureAwait(false);
                handled = true;
            }
            else if (reader.TokenType == JsonTokenType.StartObject)
            {
                topLevelObject = true;
                while (await reader.ReadAsync(ct).ConfigureAwait(false) && reader.TokenType != JsonTokenType.EndObject)
                {
                    var name = reader.PropertyName ?? "";
                    if (!await reader.ReadAsync(ct).ConfigureAwait(false)) throw new JsonException("Unexpected end of JSON.");

                    if (reader.TokenType == JsonTokenType.StartArray && Array.IndexOf(wrapperNames, name) >= 0 && !handled)
                    {
                        await StreamElementsAsync(reader, onRecord, onBytesConsumed, ct).ConfigureAwait(false);
                        handled = true;
                    }
                    else if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                    {
                        await reader.SkipContainerAsync(ct).ConfigureAwait(false);
                    }
                    onBytesConsumed(reader.BytesConsumed);
                }
            }
            else
            {
                throw new InvalidDataException("Expected a JSON array or object at the top level.");
            }
            readTime = reader.ReadTime;
        }

        if (!handled && topLevelObject)
        {
            // A lone record object: bounded in size, so a plain DOM parse is fine (second pass over the same file).
            await using var stream = source.Open();
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            if (looksLikeSingleRecord(doc.RootElement))
            {
                onRecord(doc.RootElement);
                handled = true;
            }
        }

        return new Result(handled, readTime);
    }

    private static async Task StreamElementsAsync(JsonStreamReader reader, Action<JsonElement> onRecord, Action<long> onBytes, CancellationToken ct)
    {
        while (true)
        {
            // The document points into the reader's buffer: finish with it before awaiting the reader again.
            using var doc = await reader.ReadArrayElementAsync(ct).ConfigureAwait(false);
            if (doc is null) return;
            onRecord(doc.RootElement);
            onBytes(reader.BytesConsumed);
            ct.ThrowIfCancellationRequested();
        }
    }
}
