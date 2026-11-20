using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

namespace Struo.Tests.Api;

/// <summary>A request body whose every read fails with the given exception.</summary>
internal sealed class FailingBodyStream(Exception failure) : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw failure;
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => throw failure;
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>A request body that counts the bytes read from it.</summary>
internal sealed class CountingBodyStream(Stream inner) : Stream
{
    private long _bytes;

    public long Bytes => Interlocked.Read(ref _bytes);

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
        Count(await inner.ReadAsync(buffer, ct));
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private int Count(int read)
    {
        Interlocked.Add(ref _bytes, read);
        return read;
    }
}

/// <summary>A request-size feature that records what it is set to.</summary>
internal sealed class RecordingLimitFeature(bool isReadOnly = false) : IHttpMaxRequestBodySizeFeature
{
    public bool IsReadOnly { get; } = isReadOnly;
    public long? MaxRequestBodySize { get; set; } = 30_000_000;
}

/// <summary>A logger that keeps what it is asked to write.</summary>
internal sealed class ListLogger : ILogger
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
}

/// <summary>A multipart body with one file part of the given size, generated as it is read.</summary>
internal sealed class GeneratedMultipart(long fileBytes) : Stream
{
    private readonly byte[] _head = Encoding.ASCII.GetBytes(
        "--xyz\r\nContent-Disposition: form-data; name=\"file\"; filename=\"big.bin\"\r\n" +
        "Content-Type: application/octet-stream\r\n\r\n");
    private readonly byte[] _tail = Encoding.ASCII.GetBytes("\r\n--xyz--\r\n");
    private long _position;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _head.Length + fileBytes + _tail.Length;
    public override long Position { get => _position; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var written = 0;
        while (written < buffer.Length && _position < Length)
        {
            if (_position < _head.Length)
            {
                var n = (int)Math.Min(buffer.Length - written, _head.Length - _position);
                _head.AsSpan((int)_position, n).CopyTo(buffer[written..]);
                written += n;
                _position += n;
            }
            else if (_position < _head.Length + fileBytes)
            {
                var n = (int)Math.Min(buffer.Length - written, _head.Length + fileBytes - _position);
                buffer.Slice(written, n).Fill((byte)'a');
                written += n;
                _position += n;
            }
            else
            {
                var at = (int)(_position - _head.Length - fileBytes);
                var n = Math.Min(buffer.Length - written, _tail.Length - at);
                _tail.AsSpan(at, n).CopyTo(buffer[written..]);
                written += n;
                _position += n;
            }
        }

        return written;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
        new(Read(buffer.Span));

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
