// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using IceRpc.Transports;
using System.Buffers;
using System.Diagnostics;
using System.IO.Compression;
using System.IO.Pipelines;

namespace IceRpc.Compressor;

/// <summary>A payload writer decorator that compresses the data written to it. The write with endStream set to true
/// completes the compression and carries the compression trailer. Complete writes no data to the decoratee: it only
/// completes it, with the same exception (if any). As a result, completing this writer without an exception requires
/// a prior WriteAsync with endStream set to true, unless the decoratee no longer accepts writes.</summary>
internal class CompressorPipeWriter : ReadOnlySequencePipeWriter
{
    // The maximum number of bytes given to the compression stream in one write; the compressed data this write
    // produces is drained into the decoratee before the next chunk, so _compressedDataPipe never holds more than the
    // compressed output of one chunk.
    private const int MaxChunkSize = 32 * 1024;

    // Receives the compressed data written by _compressionStream; DrainAsync empties it into _decoratee.
    private readonly Pipe _compressedDataPipe;

    // The Brotli or Deflate stream that WriteAsync feeds with the data to compress; it writes the compressed data
    // into _compressedDataPipe.
    private readonly Stream _compressionStream;

    private readonly PipeWriter _decoratee;
    private bool _isCompleted;

    // An endStream write completed the compression and disposed _compressionStream: writing is now a caller error.
    private bool _isCompressionCompleted;

    // The decoratee reported that writing is completed (the peer is no longer reading): writing is futile but
    // allowed, per the PipeWriter convention.
    private bool _isWritesClosed;

    // CompressorPipeWriter does not support the GetMemory/GetSpan + Advance API nor CancelPendingFlush: the IceRPC
    // core writes payloads with WriteAsync, and when the application code installs a payload writer interceptor, this
    // interceptor should never call these methods on "next".
    public override void Advance(int bytes) => throw new NotSupportedException();

    public override void CancelPendingFlush() => throw new NotSupportedException();

    public override void Complete(Exception? exception = null)
    {
        if (!_isCompleted)
        {
            if (exception is null && !_isCompressionCompleted && !_isWritesClosed)
            {
                throw new InvalidOperationException(
                    $"Completing a {nameof(CompressorPipeWriter)} without an exception is only allowed once the compression is completed by a WriteAsync call with endStream set to true.");
            }

            _isCompleted = true;

            _decoratee.Complete(exception);

            if (!_isCompressionCompleted)
            {
                // Releases the compression stream; the trailer bytes it writes remain in _compressedDataPipe and
                // are discarded.
                _compressionStream.Dispose();
            }

            _compressedDataPipe.Writer.Complete();
            _compressedDataPipe.Reader.Complete();
        }
    }

    public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default) =>
        WriteAsync(ReadOnlySequence<byte>.Empty, endStream: false, cancellationToken);

    public override Memory<byte> GetMemory(int sizeHint = 0) => throw new NotSupportedException();

    public override Span<byte> GetSpan(int sizeHint = 0) => throw new NotSupportedException();

    public override ValueTask<FlushResult> WriteAsync(
        ReadOnlyMemory<byte> source,
        CancellationToken cancellationToken = default) =>
        WriteAsync(new ReadOnlySequence<byte>(source), endStream: false, cancellationToken);

    public override async ValueTask<FlushResult> WriteAsync(
        ReadOnlySequence<byte> source,
        bool endStream,
        CancellationToken cancellationToken)
    {
        if (_isCompleted || _isCompressionCompleted)
        {
            throw new InvalidOperationException("Writing is not allowed once the writer is completed.");
        }

        foreach (ReadOnlyMemory<byte> segment in source)
        {
            for (int offset = 0; offset < segment.Length; offset += MaxChunkSize)
            {
                // This write goes to _compressedDataPipe, in memory: it can't block, so it doesn't need the
                // cancellation token.
                await _compressionStream.WriteAsync(
                    segment.Slice(offset, Math.Min(MaxChunkSize, segment.Length - offset)),
                    CancellationToken.None).ConfigureAwait(false);

                _ = await DrainAsync(endStream: false, cancellationToken).ConfigureAwait(false);
            }
        }

        if (endStream)
        {
            // Finishes the compression: this writes the compression trailer into _compressedDataPipe.
            await _compressionStream.DisposeAsync().ConfigureAwait(false);
            _isCompressionCompleted = true;
        }
        else
        {
            await _compressionStream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }

        return await DrainAsync(endStream, cancellationToken).ConfigureAwait(false);
    }

    internal CompressorPipeWriter(
        PipeWriter decoratee,
        CompressionFormat compressionFormat,
        CompressionLevel compressionLevel)
    {
        Debug.Assert(compressionFormat is CompressionFormat.Brotli or CompressionFormat.Deflate);

        _decoratee = decoratee;

        // The compression stream writes the compressed data into _compressedDataPipe; pauseWriterThreshold: 0 so
        // that these writes never block.
        _compressedDataPipe = new Pipe(new PipeOptions(pauseWriterThreshold: 0, useSynchronizationContext: false));

        // leaveOpen: true because this class completes _compressedDataPipe.Writer itself.
        Stream compressedDataStream = _compressedDataPipe.Writer.AsStream(leaveOpen: true);

        _compressionStream = compressionFormat == CompressionFormat.Brotli ?
            new BrotliStream(compressedDataStream, compressionLevel) :
            new DeflateStream(compressedDataStream, compressionLevel);
    }

    /// <summary>Writes the contents of _compressedDataPipe to the decoratee; when the pipe is empty, flushes the
    /// decoratee instead to surface its state (for example, writes closed by the peer).</summary>
    private async ValueTask<FlushResult> DrainAsync(bool endStream, CancellationToken cancellationToken)
    {
        FlushResult flushResult;
        if (_compressedDataPipe.Reader.TryRead(out ReadResult readResult))
        {
            try
            {
                flushResult = await _decoratee.WriteAsync(readResult.Buffer, endStream, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _compressedDataPipe.Reader.AdvanceTo(readResult.Buffer.End);
            }
        }
        else
        {
            Debug.Assert(!endStream); // completing the compression always produces trailer bytes
            flushResult = await _decoratee.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        if (flushResult.IsCompleted)
        {
            // The decoratee no longer accepts writes; a graceful Complete can follow without an endStream write.
            _isWritesClosed = true;
        }
        return flushResult;
    }
}
