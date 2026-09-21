using System.Diagnostics;
using System.Text.Json;

namespace FlightPathPlanner.Services.Import;

/// <summary>Forward-only async JSON reader over a <see cref="Stream"/>. Only the bytes of the value currently being read are
/// buffered, so a multi-hundred-MB array of records is processed one record at a time instead of being materialised as a DOM.
///
/// Usage contract: a <see cref="JsonDocument"/> returned by <see cref="ReadArrayElementAsync"/>/<see cref="ReadValueAsync"/>
/// points into the reader's buffer. Dispose it (or finish using it) BEFORE the next call that awaits the reader.</summary>
public sealed class JsonStreamReader : IDisposable
{
    private const int MaxBufferBytes = 1 << 30; // a single JSON value larger than 1 GiB is not a record we can handle

    private readonly Stream _stream;
    private byte[] _buffer;
    private int _start;
    private int _end;
    private JsonReaderState _state;
    private bool _eof;
    private long _consumedBeforeBuffer; // bytes already discarded from the front of the buffer

    public JsonStreamReader(Stream stream, int initialBufferBytes = 1 << 16)
    {
        _stream = stream;
        _buffer = new byte[Math.Max(initialBufferBytes, 1024)];
        _state = new JsonReaderState(new JsonReaderOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
    }

    /// <summary>Type of the token most recently returned by <see cref="ReadAsync"/>.</summary>
    public JsonTokenType TokenType { get; private set; }

    /// <summary>Name of the property when <see cref="TokenType"/> is <see cref="JsonTokenType.PropertyName"/>.</summary>
    public string? PropertyName { get; private set; }

    /// <summary>Bytes of the input consumed so far (drives byte-based progress).</summary>
    public long BytesConsumed => _consumedBeforeBuffer + _start;

    /// <summary>Total time spent waiting on the underlying stream.</summary>
    public TimeSpan ReadTime { get; private set; }

    /// <summary>Advances to the next token. Returns false at the end of the input.</summary>
    public async ValueTask<bool> ReadAsync(CancellationToken ct)
    {
        while (true)
        {
            var result = TryReadToken();
            if (result == Step.Done) return true;
            if (result == Step.End) return false;
            await FillAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>Reads the next array element as a document, or returns null when the closing ']' is reached.</summary>
    public async ValueTask<JsonDocument?> ReadArrayElementAsync(CancellationToken ct)
    {
        while (true)
        {
            var result = TryReadValue(allowEndArray: true, materialize: true, out var doc);
            if (result == Step.Done) return doc;
            if (result == Step.End) return null;
            await FillAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>Reads the next complete value (object, array or scalar) as a document.</summary>
    public async ValueTask<JsonDocument> ReadValueAsync(CancellationToken ct)
    {
        while (true)
        {
            var result = TryReadValue(allowEndArray: false, materialize: true, out var doc);
            if (result == Step.Done) return doc!;
            await FillAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>Skips the next complete value without materialising it.</summary>
    public async ValueTask SkipValueAsync(CancellationToken ct)
    {
        while (true)
        {
            var result = TryReadValue(allowEndArray: false, materialize: false, out _);
            if (result == Step.Done) return;
            await FillAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>Consumes tokens until the container whose start token was just read has been closed.</summary>
    public async ValueTask SkipContainerAsync(CancellationToken ct)
    {
        int depth = 1;
        while (depth > 0)
        {
            if (!await ReadAsync(ct).ConfigureAwait(false)) throw new JsonException("Unexpected end of JSON while skipping a value.");
            if (TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) depth++;
            else if (TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray) depth--;
        }
    }

    private enum Step { NeedMore, Done, End }

    private Step TryReadToken()
    {
        var reader = new Utf8JsonReader(new ReadOnlySpan<byte>(_buffer, _start, _end - _start), _eof, _state);
        if (reader.Read())
        {
            TokenType = reader.TokenType;
            PropertyName = TokenType == JsonTokenType.PropertyName ? reader.GetString() : null;
            _state = reader.CurrentState;
            _start += (int)reader.BytesConsumed;
            return Step.Done;
        }

        _state = reader.CurrentState;
        _start += (int)reader.BytesConsumed;
        return _eof ? Step.End : Step.NeedMore;
    }

    private Step TryReadValue(bool allowEndArray, bool materialize, out JsonDocument? doc)
    {
        doc = null;
        // A fresh reader over the unread bytes each attempt: if the value is incomplete we just retry after the next fill.
        var reader = new Utf8JsonReader(new ReadOnlySpan<byte>(_buffer, _start, _end - _start), _eof, _state);
        if (!reader.Read()) return _eof ? Step.End : Step.NeedMore;

        if (allowEndArray && reader.TokenType == JsonTokenType.EndArray)
        {
            _state = reader.CurrentState;
            _start += (int)reader.BytesConsumed;
            return Step.End;
        }

        int valueStart = (int)reader.TokenStartIndex;
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray && !reader.TrySkip()) return Step.NeedMore;

        int valueEnd = (int)reader.BytesConsumed;
        if (materialize) doc = JsonDocument.Parse(new ReadOnlyMemory<byte>(_buffer, _start + valueStart, valueEnd - valueStart));
        _state = reader.CurrentState;
        _start += valueEnd;
        return Step.Done;
    }

    private async ValueTask FillAsync(CancellationToken ct)
    {
        if (_eof) throw new JsonException("Unexpected end of JSON input.");

        if (_start > 0)
        {
            Buffer.BlockCopy(_buffer, _start, _buffer, 0, _end - _start);
            _consumedBeforeBuffer += _start;
            _end -= _start;
            _start = 0;
        }
        if (_end == _buffer.Length)
        {
            if (_buffer.Length >= MaxBufferBytes) throw new JsonException("A single JSON value is too large to read.");
            Array.Resize(ref _buffer, _buffer.Length * 2);
        }

        var timer = Stopwatch.StartNew();
        int read = await _stream.ReadAsync(_buffer.AsMemory(_end), ct).ConfigureAwait(false);
        ReadTime += timer.Elapsed;
        if (read == 0) _eof = true; else _end += read;
    }

    public void Dispose() => _stream.Dispose();
}
