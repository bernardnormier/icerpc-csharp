// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Slice.Internal
{
    /// <summary>Extension methods to decode payloads carried by a PipeReader.</summary>
    internal static class PipeReaderExtensions
    {
        private const int MaxSegmentSize = 4 * 1024 * 1024; // TODO: make MaxSegmentSize configurable

        /// <summary>Reads a Slice segment from a pipe reader.</summary>
        /// <param name="reader">The pipe reader.</param>
        /// <param name="encoding">The encoding.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>A read result with the segment read from the reader unless <see cref="ReadResult.IsCanceled"/> is
        /// <c>true</c>.</returns>
        /// <exception cref="InvalidDataException">Thrown when the segment size could not be decoded or the segment size
        /// exceeds the max segment size.</exception>
        /// <remarks>The caller must call AdvanceTo on the reader, as usual. With encoding 1.1, this method reads all
        /// the remaining bytes in the reader; otherwise, this method reads the segment size in the segment and returns
        /// exactly segment size bytes. This method often examines the buffer it returns as part of ReadResult,
        /// therefore the caller should never examine less than Buffer.End.</remarks>
        internal static async ValueTask<ReadResult> ReadSegmentAsync(
            this PipeReader reader,
            SliceEncoding encoding,
            CancellationToken cancel)
        {
            // This method does not attempt to read the reader synchronously. A caller that wants a sync attempt can
            // call TryReadSegment.

            if (encoding == SliceEncoding.Slice11)
            {
                // We read everything up to the MaxSegmentSize + 1.
                // It's MaxSegmentSize + 1 and not MaxSegmentSize because if the segment's size is MaxSegmentSize,
                // we could get readResult.IsCompleted == false even though the full segment was read.

                ReadResult readResult = await reader.ReadAtLeastAsync(MaxSegmentSize + 1, cancel).ConfigureAwait(false);

                if (readResult.IsCompleted && readResult.Buffer.Length <= MaxSegmentSize)
                {
                    return readResult;
                }
                else
                {
                    reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                    throw new InvalidDataException("segment size exceeds maximum value");
                }
            }
            else
            {
                ReadResult readResult;
                int segmentSize;

                while (true)
                {
                    readResult = await reader.ReadAsync(cancel).ConfigureAwait(false);

                    try
                    {
                        if (IsCompleteSegment(ref readResult, out segmentSize, out long consumed))
                        {
                            return readResult;
                        }
                        else if (segmentSize >= 0)
                        {
                            Debug.Assert(segmentSize > 0);
                            Debug.Assert(consumed > 0);

                            // We decoded the segmentSize and examined the whole buffer but it was not sufficient.
                            reader.AdvanceTo(readResult.Buffer.GetPosition(consumed), readResult.Buffer.End);
                            break; // while
                        }
                        else
                        {
                            Debug.Assert(!readResult.IsCompleted); // see IsCompleteSegment
                            reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                            // and continue loop with at least one additional byte
                        }
                    }
                    catch
                    {
                        // A ReadAsync or TryRead method that throws an exception should not leave the reader in a
                        // "reading" state.
                        reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                        throw;
                    }
                }

                readResult = await reader.ReadAtLeastAsync(segmentSize, cancel).ConfigureAwait(false);

                if (readResult.IsCanceled)
                {
                    return readResult;
                }

                if (readResult.Buffer.Length < segmentSize)
                {
                    Debug.Assert(readResult.IsCompleted);
                    reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                    throw new InvalidDataException($"payload has fewer than '{segmentSize}' bytes");
                }

                return readResult.Buffer.Length == segmentSize ? readResult :
                    new ReadResult(readResult.Buffer.Slice(0, segmentSize), isCanceled: false, isCompleted: false);
            }
        }

        /// <summary>Attempts to read a Slice segment from a pipe reader.</summary>
        /// <param name="reader">The pipe reader.</param>
        /// <param name="encoding">The encoding.</param>
        /// <param name="readResult">The read result.</param>
        /// <returns><c>true</c> when <paramref name="readResult"/> contains the segment read synchronously, or the
        /// call was cancelled; otherwise, <c>false</c>.</returns>
        /// <exception cref="InvalidDataException">Thrown when the segment size could not be decoded or the segment size
        /// exceeds the max segment size.</exception>
        /// <remarks>When this method returns <c>true</c>, the caller must call AdvanceTo on the reader, as usual. This
        /// method often examines the buffer it returns as part of ReadResult, therefore the caller should never
        /// examine less than Buffer.End when the return value is <c>true</c>. When this method returns <c>false</c>,
        /// the caller must call <see cref="ReadSegmentAsync"/>.</remarks>
        internal static bool TryReadSegment(this PipeReader reader, SliceEncoding encoding, out ReadResult readResult)
        {
            if (encoding == SliceEncoding.Slice11)
            {
                if (reader.TryRead(out readResult))
                {
                    if (readResult.IsCanceled)
                    {
                        return true; // and the buffer does not matter
                    }

                    if (readResult.Buffer.Length > MaxSegmentSize)
                    {
                        reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                        throw new InvalidDataException(
                            $"segment size '{readResult.Buffer.Length}' exceeds maximum value");
                    }

                    if (readResult.IsCompleted)
                    {
                        return true;
                    }
                    else
                    {
                        // don't consume anything but mark the whole buffer as examined - we need more.
                        reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                    }
                }

                readResult = default;
                return false;
            }
            else
            {
                if (reader.TryRead(out readResult))
                {
                    try
                    {
                        if (IsCompleteSegment(ref readResult, out int _, out long _))
                        {
                            return true;
                        }
                        else
                        {
                            // we don't consume anything but examined the whole buffer since it's not sufficient.
                            reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                            readResult = default;
                            return false;
                        }
                    }
                    catch
                    {
                        reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                        throw;
                    }
                }
                else
                {
                    return false;
                }
            }
        }

        /// <summary>Checks if a read result holds a complete Slice segment.</summary>
        /// <returns><c>true</c> when <paramref name="readResult"/> holds a complete segment or is canceled; otherwise,
        /// <c>false</c>.</returns>
        /// <remarks><paramref name="segmentSize"/> and <paramref name="consumed"/> can be set when this method returns
        /// <c>false</c>. In this case, both segmentSize and consumed are greater than 0.</remarks>
        private static bool IsCompleteSegment(ref ReadResult readResult, out int segmentSize, out long consumed)
        {
            consumed = 0;
            segmentSize = -1;

            if (readResult.IsCanceled)
            {
                return true; // and buffer etc. does not matter
            }

            if (readResult.Buffer.IsEmpty)
            {
                Debug.Assert(readResult.IsCompleted);
                segmentSize = 0;
                return true; // the caller will call AdvanceTo on this buffer.
            }

            var decoder = new SliceDecoder(readResult.Buffer, SliceEncoding.Slice20);
            if (decoder.TryDecodeSize(out segmentSize))
            {
                consumed = decoder.Consumed;

                if (segmentSize > MaxSegmentSize)
                {
                    throw new InvalidDataException($"segment size '{segmentSize}' exceeds maximum value");
                }

                if (readResult.Buffer.Length >= consumed + segmentSize)
                {
                    // When segmentSize is 0, we return a read result with an empty buffer.
                    readResult = new ReadResult(
                        readResult.Buffer.Slice(readResult.Buffer.GetPosition(consumed), segmentSize),
                        isCanceled: false,
                        isCompleted: readResult.IsCompleted &&
                            readResult.Buffer.Length == consumed + segmentSize);

                    return true;
                }

                if (readResult.IsCompleted && consumed + segmentSize > readResult.Buffer.Length)
                {
                    throw new InvalidDataException($"payload has fewer than '{segmentSize}' bytes");
                }

                // segmentSize and consumed are set and can be used by the caller.
                return false;
            }
            else if (readResult.IsCompleted)
            {
                throw new InvalidDataException("received Slice segment with truncated size");
            }
            else
            {
                segmentSize = -1;
                return false;
            }
        }
    }
}
