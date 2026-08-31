// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Tests.Common;
using NUnit.Framework;
using System.Buffers;
using System.Collections.Immutable;
using System.IO.Compression;
using System.IO.Pipelines;

namespace IceRpc.Compressor.Tests;

public class CompressorInterceptorTests
{
    private static readonly byte[] _payload =
        Enumerable.Range(0, 4096).Select(i => (byte)(i % 256)).ToArray();

    /// <summary>Verifies that the compressor interceptor installs a payload writer interceptor that compresses the
    /// input using the given compression format when the request carries the compress payload feature.</summary>
    [Test]
    public async Task Compress_request_payload(
        [Values(CompressionFormat.Brotli, CompressionFormat.Deflate)] CompressionFormat compressionFormat)
    {
        // Arrange
        var invoker = new InlineInvoker((request, cancellationToken) =>
            Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance)));
        var sut = new CompressorInterceptor(invoker, compressionFormat);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        request.Features = request.Features.With(CompressFeature.Compress);
        var outStream = new MemoryStream();
        var output = PipeWriter.Create(outStream);

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        var payloadWriter = (Transports.ReadOnlySequencePipeWriter)request.GetPayloadWriter(output);
        await payloadWriter.WriteAsync(new ReadOnlySequence<byte>(_payload), endStream: true, default);

        // Rewind the out stream and check that it was correctly compressed.
        outStream.Seek(0, SeekOrigin.Begin);
        using Stream decompressedStream = compressionFormat == CompressionFormat.Brotli ?
            new BrotliStream(outStream, CompressionMode.Decompress) :
            new DeflateStream(outStream, CompressionMode.Decompress);
        var decompressedPayload = new byte[4096];
        await decompressedStream.ReadAtLeastAsync(decompressedPayload, _payload.Length);
        Assert.That(decompressedPayload, Is.EqualTo(_payload));
        payloadWriter.Complete();
    }

    /// <summary>Verifies that completing the compressor's payload writer with an exception completes the decorated
    /// payload writer with an exception (and not gracefully with a valid compression trailer).</summary>
    [Test]
    public async Task Complete_compressor_payload_writer_with_exception_completes_output_with_exception(
        [Values(CompressionFormat.Brotli, CompressionFormat.Deflate)] CompressionFormat compressionFormat)
    {
        // Arrange
        var invoker = new InlineInvoker((request, cancellationToken) =>
            Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance)));
        var sut = new CompressorInterceptor(invoker, compressionFormat);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        request.Features = request.Features.With(CompressFeature.Compress);
        await sut.InvokeAsync(request, default);

        var pipe = new Pipe();
        PipeWriter payloadWriter = request.GetPayloadWriter(pipe.Writer);
        await payloadWriter.WriteAsync(_payload);

        // Act
        payloadWriter.Complete(new InvalidOperationException("payload copy failed"));

        // Assert
        Assert.That(
            async () =>
            {
                while (true)
                {
                    ReadResult readResult = await pipe.Reader.ReadAsync();
                    pipe.Reader.AdvanceTo(readResult.Buffer.End);
                    if (readResult.IsCompleted)
                    {
                        break;
                    }
                }
            },
            Throws.InstanceOf<InvalidOperationException>());
        pipe.Reader.Complete();
    }

    /// <summary>Verifies that a WriteAsync with endStream set to true writes the full compressed payload — including
    /// the compression trailer — and that the completion that follows writes nothing more.</summary>
    [Test]
    public async Task Write_with_end_stream_writes_the_compression_trailer(
        [Values(CompressionFormat.Brotli, CompressionFormat.Deflate)] CompressionFormat compressionFormat)
    {
        // Arrange
        var invoker = new InlineInvoker((request, cancellationToken) =>
            Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance)));
        var sut = new CompressorInterceptor(invoker, compressionFormat);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        request.Features = request.Features.With(CompressFeature.Compress);
        await sut.InvokeAsync(request, default);

        var output = new RecordingPipeWriter();
        var payloadWriter = (Transports.ReadOnlySequencePipeWriter)request.GetPayloadWriter(output);

        // Act
        await payloadWriter.WriteAsync(new ReadOnlySequence<byte>(_payload), endStream: true, default);
        payloadWriter.Complete();

        // Assert
        Assert.That(output.EndStream, Is.True);
        Assert.That(output.WriteCallsAfterEndStream, Is.Zero);
        using Stream decompressedStream = compressionFormat == CompressionFormat.Brotli ?
            new BrotliStream(new MemoryStream(output.WrittenBytes), CompressionMode.Decompress) :
            new DeflateStream(new MemoryStream(output.WrittenBytes), CompressionMode.Decompress);
        var decompressedPayload = new MemoryStream();
        decompressedStream.CopyTo(decompressedPayload); // throws if the compressed data is truncated
        Assert.That(decompressedPayload.ToArray(), Is.EqualTo(_payload));
    }

    /// <summary>Verifies the compression of a payload much larger than the compressor's internal chunk size, using
    /// incompressible data.</summary>
    [Test]
    public async Task Compress_large_payload(
        [Values(CompressionFormat.Brotli, CompressionFormat.Deflate)] CompressionFormat compressionFormat)
    {
        // Arrange
        byte[] payload = new byte[1024 * 1024];
        new Random(42).NextBytes(payload);
        var invoker = new InlineInvoker((request, cancellationToken) =>
            Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance)));
        var sut = new CompressorInterceptor(invoker, compressionFormat);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        request.Features = request.Features.With(CompressFeature.Compress);
        await sut.InvokeAsync(request, default);

        var output = new RecordingPipeWriter();
        var payloadWriter = (Transports.ReadOnlySequencePipeWriter)request.GetPayloadWriter(output);

        // Act
        await payloadWriter.WriteAsync(new ReadOnlySequence<byte>(payload), endStream: true, default);
        payloadWriter.Complete();

        // Assert
        using Stream decompressedStream = compressionFormat == CompressionFormat.Brotli ?
            new BrotliStream(new MemoryStream(output.WrittenBytes), CompressionMode.Decompress) :
            new DeflateStream(new MemoryStream(output.WrittenBytes), CompressionMode.Decompress);
        var decompressedPayload = new MemoryStream();
        decompressedStream.CopyTo(decompressedPayload);
        Assert.That(decompressedPayload.ToArray(), Is.EqualTo(payload));
    }

    /// <summary>Verifies that once the decoratee reports that writing is completed (the peer stopped reading), later
    /// writes return a completed flush result and a graceful completion without endStream is allowed.</summary>
    [Test]
    public async Task Complete_without_end_stream_after_writes_closed_does_not_throw()
    {
        // Arrange
        var invoker = new InlineInvoker((request, cancellationToken) =>
            Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance)));
        var sut = new CompressorInterceptor(invoker, CompressionFormat.Brotli);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        request.Features = request.Features.With(CompressFeature.Compress);
        await sut.InvokeAsync(request, default);

        var output = new RecordingPipeWriter() { IsWritesClosed = true };
        PipeWriter payloadWriter = request.GetPayloadWriter(output);

        // Act
        FlushResult flushResult = await payloadWriter.WriteAsync(_payload);

        // Assert
        Assert.That(flushResult.IsCompleted, Is.True);
        flushResult = await payloadWriter.WriteAsync(_payload);
        Assert.That(flushResult.IsCompleted, Is.True);
        Assert.That(() => payloadWriter.Complete(), Throws.Nothing);
    }

    /// <summary>Verifies that completing the compressor's payload writer without an exception throws when the
    /// compression was not completed by a WriteAsync with endStream set to true.</summary>
    [Test]
    public async Task Complete_without_end_stream_throws(
        [Values(CompressionFormat.Brotli, CompressionFormat.Deflate)] CompressionFormat compressionFormat)
    {
        // Arrange
        var invoker = new InlineInvoker((request, cancellationToken) =>
            Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance)));
        var sut = new CompressorInterceptor(invoker, compressionFormat);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        request.Features = request.Features.With(CompressFeature.Compress);
        await sut.InvokeAsync(request, default);

        var pipe = new Pipe();
        PipeWriter payloadWriter = request.GetPayloadWriter(pipe.Writer);
        await payloadWriter.WriteAsync(_payload);

        // Act/Assert
        Assert.That(() => payloadWriter.Complete(), Throws.InvalidOperationException);

        // Cleanup
        payloadWriter.Complete(new OperationCanceledException());
        pipe.Reader.Complete();
    }

    /// <summary>Verifies that the compressor interceptor does not install a payload writer interceptor if the request
    /// does not contain the compress payload feature.</summary>
    [Test]
    public async Task Compressor_interceptor_without_the_compress_feature_does_not_install_a_payload_writer_interceptor()
    {
        var invoker = new InlineInvoker((request, cancellationToken) =>
            Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance)));
        var sut = new CompressorInterceptor(invoker, CompressionFormat.Brotli);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));

        await sut.InvokeAsync(request, default);

        var pipe = new Pipe();
        Assert.That(request.GetPayloadWriter(pipe.Writer), Is.EqualTo(pipe.Writer));
        pipe.Reader.Complete();
        pipe.Writer.Complete();
    }

    /// <summary>Verifies that the compressor interceptor does not install a payload writer interceptor if the request
    /// is already compressed (the request already has a compression format field).</summary>
    [Test]
    public async Task Compressor_interceptor_does_not_install_a_payload_writer_interceptor_if_the_request_is_already_compressed()
    {
        var invoker = new InlineInvoker((request, cancellationToken) =>
            Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance)));
        var sut = new CompressorInterceptor(invoker, CompressionFormat.Brotli);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        request.Features = request.Features.With(CompressFeature.Compress);
        request.Fields = request.Fields.With(
            RequestFieldKey.CompressionFormat,
            new ReadOnlySequence<byte>(new byte[] { (byte)CompressionFormat.Brotli }));

        await sut.InvokeAsync(request, default);

        var pipe = new Pipe();
        Assert.That(request.GetPayloadWriter(pipe.Writer), Is.EqualTo(pipe.Writer));
        pipe.Reader.Complete();
        pipe.Writer.Complete();
    }

    /// <summary>Verifies that the compressor interceptor does not update the response payload when the compression
    /// format is not supported, and lets the response pass through unchanged.</summary>
    [Test]
    public async Task Compressor_interceptor_lets_responses_with_unsupported_compression_format_pass_through()
    {
        PipeReader? initialPayload = null;
        var invoker = new InlineInvoker((request, cancellationToken) =>
        {
            IncomingResponse response = CreateResponseWithCompressionFormat(request, (CompressionFormat)255);
            initialPayload = response.Payload;
            return Task.FromResult(response);
        });
        var sut = new CompressorInterceptor(invoker, CompressionFormat.Brotli);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));

        IncomingResponse response = await sut.InvokeAsync(request, default);

        Assert.That(response.Payload, Is.EqualTo(initialPayload));
    }

    /// <summary>Verifies that the compressor interceptor wraps the response payload with a pipe reader that
    /// decompress it, when the response carries a supported compression format field.</summary>
    [Test]
    public async Task Decompress_response_payload(
        [Values(CompressionFormat.Brotli, CompressionFormat.Deflate)] CompressionFormat compressionFormat)
    {
        var invoker = new InlineInvoker((request, cancellationToken) =>
        {
            IncomingResponse response = CreateResponseWithCompressionFormat(request, compressionFormat);
            response.Payload = PipeReader.Create(CreateCompressedPayload(_payload, compressionFormat));
            return Task.FromResult(response);
        });
        var sut = new CompressorInterceptor(invoker, compressionFormat);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));

        IncomingResponse response = await sut.InvokeAsync(request, default);

        ReadResult readResult = await response.Payload.ReadAsync();
        Assert.That(readResult.Buffer.ToArray(), Is.EqualTo(_payload));
    }

    private static IncomingResponse CreateResponseWithCompressionFormat(
        OutgoingRequest request,
        CompressionFormat compressionFormat) =>
        new(
            request,
            FakeConnectionContext.Instance,
            StatusCode.Ok,
            errorMessage: null,
            new Dictionary<ResponseFieldKey, ReadOnlySequence<byte>>
            {
                [ResponseFieldKey.CompressionFormat] =
                    new ReadOnlySequence<byte>(new byte[] { (byte)compressionFormat })
            }.ToImmutableDictionary());

    private static Stream CreateCompressedPayload(byte[] data, CompressionFormat compressionFormat)
    {
        if (compressionFormat != CompressionFormat.Brotli && compressionFormat != CompressionFormat.Deflate)
        {
            throw new ArgumentException(
                $"Compression format '{compressionFormat}' not supported",
                nameof(compressionFormat));
        }

        var outStream = new MemoryStream();
        {
            using Stream compressedStream = compressionFormat == CompressionFormat.Brotli ?
                new BrotliStream(outStream, CompressionMode.Compress, true) :
                new DeflateStream(outStream, CompressionMode.Compress, true);
            using var payload = new MemoryStream(data);
            payload.CopyTo(compressedStream);
        }
        outStream.Seek(0, SeekOrigin.Begin);
        return outStream;
    }

    // A ReadOnlySequencePipeWriter that records the written bytes and whether endStream was received. When
    // IsWritesClosed is set, all writes and flushes return a completed flush result.
    private class RecordingPipeWriter : Transports.ReadOnlySequencePipeWriter
    {
        internal bool EndStream { get; private set; }

        internal bool IsWritesClosed { get; set; }

        internal int WriteCallsAfterEndStream { get; private set; }

        internal byte[] WrittenBytes => _data.ToArray();

        private readonly List<byte> _data = new();

        public override void Advance(int bytes) => throw new NotSupportedException();

        public override void CancelPendingFlush() => throw new NotSupportedException();

        public override void Complete(Exception? exception = null)
        {
        }

        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default) =>
            new(new FlushResult(isCanceled: false, isCompleted: IsWritesClosed));

        public override Memory<byte> GetMemory(int sizeHint = 0) => throw new NotSupportedException();

        public override Span<byte> GetSpan(int sizeHint = 0) => throw new NotSupportedException();

        public override ValueTask<FlushResult> WriteAsync(
            ReadOnlyMemory<byte> source,
            CancellationToken cancellationToken = default) =>
            WriteAsync(new ReadOnlySequence<byte>(source), endStream: false, cancellationToken);

        public override ValueTask<FlushResult> WriteAsync(
            ReadOnlySequence<byte> source,
            bool endStream,
            CancellationToken cancellationToken)
        {
            if (EndStream)
            {
                WriteCallsAfterEndStream++;
            }
            foreach (ReadOnlyMemory<byte> buffer in source)
            {
                _data.AddRange(buffer.Span);
            }
            EndStream |= endStream;
            return new(new FlushResult(isCanceled: false, isCompleted: IsWritesClosed));
        }
    }
}
