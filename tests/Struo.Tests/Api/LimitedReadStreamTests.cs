using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Struo.Api.Http;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.Api;

public class LimitedReadStreamTests
{
    private sealed class ThrowingStream(Exception failure) : Stream
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

    [Fact]
    public async Task Reads_up_to_the_limit_without_exceeding()
    {
        using var stream = new LimitedReadStream(new MemoryStream(new byte[10]), 10);
        var buffer = new byte[16];

        (await stream.ReadAsync(buffer)).Should().Be(10);
    }

    [Fact]
    public async Task Throws_payload_too_large_past_the_limit()
    {
        using var stream = new LimitedReadStream(new MemoryStream(new byte[11]), 10);

        var act = async () => await stream.ReadAsync(new byte[16]);

        await act.Should().ThrowAsync<PayloadTooLargeException>();
    }

    [Fact]
    public async Task Passes_a_kestrel_413_from_the_inner_stream_through()
    {
        var kestrel = new BadHttpRequestException("too large", StatusCodes.Status413PayloadTooLarge);
        using var stream = new LimitedReadStream(new ThrowingStream(kestrel), 10);

        var act = async () => await stream.ReadAsync(new byte[16]);

        (await act.Should().ThrowAsync<BadHttpRequestException>()).Which.Should().BeSameAs(kestrel);
    }

    [Fact]
    public void Passes_a_kestrel_413_through_on_a_synchronous_read()
    {
        var kestrel = new BadHttpRequestException("too large", StatusCodes.Status413PayloadTooLarge);
        using var stream = new LimitedReadStream(new ThrowingStream(kestrel), 10);

        var act = () => stream.Read(new byte[16], 0, 16);

        act.Should().Throw<BadHttpRequestException>();
    }

    [Fact]
    public async Task Passes_another_bad_request_through()
    {
        var other = new BadHttpRequestException("bad chunk", StatusCodes.Status400BadRequest);
        using var stream = new LimitedReadStream(new ThrowingStream(other), 10);

        var act = async () => await stream.ReadAsync(new byte[16]);

        await act.Should().ThrowAsync<BadHttpRequestException>();
    }
}
