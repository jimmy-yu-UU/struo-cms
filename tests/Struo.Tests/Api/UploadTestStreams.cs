using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

namespace Struo.Tests.Api;

/// <summary>
/// A request body whose every read fails with the given exception. <c>beforeFailing</c> runs first, as when
/// the client leaves while the server reads.
/// </summary>
internal sealed class FailingBodyStream(Exception failure, Action? beforeFailing = null) : Stream
{
    private Exception Fail()
    {
        beforeFailing?.Invoke();
        return failure;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw Fail();
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => throw Fail();
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

/// <summary>A typed logger that keeps what it is asked to write.</summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly RecordingLogger _inner = new();

    public List<(LogLevel Level, string Message, Exception? Exception)> Entries => _inner.Entries;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) => _inner.Log(logLevel, eventId, state, exception, formatter);
}

/// <summary>A logger that keeps what it is asked to write, exceptions included.</summary>
internal sealed class RecordingLogger : ILogger
{
    public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception), exception));
}
