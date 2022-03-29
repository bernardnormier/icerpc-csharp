// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using System.Buffers;

namespace IceRpc.Transports.Internal
{
    internal static class SlicMemoryExtensions
    {
        internal static (FrameType, int, long?, long) DecodeHeader(this ReadOnlySequence<byte> buffer)
        {
            var decoder = new SliceDecoder(buffer, SliceEncoding.Slice20);
            var type = (FrameType)decoder.DecodeByte();
            int dataSize = decoder.DecodeSize();
            if (type < FrameType.Stream)
            {
                return (type, dataSize, null, decoder.Consumed);
            }
            else
            {
                ulong id = decoder.DecodeVarULong();
                dataSize -= SliceEncoder.GetVarULongEncodedSize(id);
                return (type, dataSize, (long)id, decoder.Consumed);
            }
        }

        // TODO: if we really want a separate method, it should go to a different class.
        internal static (uint, InitializeBody?) DecodeInitialize(this ref SliceDecoder decoder)
        {
            uint version = decoder.DecodeVarUInt();
            if (version == SlicDefinitions.V1)
            {
                return (version, new InitializeBody(ref decoder));
            }
            else
            {
                return (version, null);
            }
        }

        internal static (uint, InitializeBody?) DecodeInitialize(this ReadOnlyMemory<byte> buffer, FrameType type)
        {
            if (type != FrameType.Initialize)
            {
                throw new InvalidDataException(
                    $"unexpected Slic frame type {type}, expected {FrameType.Initialize}");
            }

            return SliceEncoding.Slice20.DecodeBuffer(new ReadOnlySequence<byte>(buffer), DecodeInitialize);
        }

        internal static (InitializeAckBody?, VersionBody?) DecodeInitializeAckOrVersion(
            this ReadOnlyMemory<byte> buffer,
            FrameType type)
        {
            var decoder = new SliceDecoder(buffer, SliceEncoding.Slice20);
            return type switch
            {
                FrameType.InitializeAck => (new InitializeAckBody(ref decoder), null),
                FrameType.Version => (null, new VersionBody(ref decoder)),
                _ => throw new InvalidDataException($"unexpected Slic frame '{type}'")
            };
        }

        internal static StreamResetBody DecodeStreamReset(this ReadOnlyMemory<byte> buffer)
        {
            var decoder = new SliceDecoder(buffer, SliceEncoding.Slice20);
            return new StreamResetBody(ref decoder);
        }

        internal static StreamConsumedBody DecodeStreamConsumed(this ReadOnlyMemory<byte> buffer)
        {
            var decoder = new SliceDecoder(buffer, SliceEncoding.Slice20);
            return new StreamConsumedBody(ref decoder);
        }

        internal static StreamStopSendingBody DecodeStreamStopSending(this ReadOnlyMemory<byte> buffer)
        {
            var decoder = new SliceDecoder(buffer, SliceEncoding.Slice20);
            return new StreamStopSendingBody(ref decoder);
        }
    }
}
