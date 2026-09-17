// What the Slice compiler would generate for:
//
// module VisitorCenter
// unchecked enum GreeterError {
//     EmptyName
//     NameTooLong(maxLength: int32)
// }

using System.ComponentModel;
using ZeroC.Slice.Codec;

namespace VisitorCenter;

internal partial union GreeterError(GreeterError.EmptyName, GreeterError.NameTooLong, GreeterError.Unknown)
    : IEquatable<GreeterError>
{
    public sealed partial record class EmptyName
    {
        public const int Discriminant = 0;

        [EditorBrowsable(EditorBrowsableState.Never)]
        internal void Encode(ref SliceEncoder encoder)
        {
            encoder.EncodeVarInt32(Discriminant);
            var sizePlaceholder = encoder.GetPlaceholderSpan(4);
            int startPos = encoder.EncodedByteCount;
            encoder.EncodeVarInt32(SliceDefinitions.TagEndMarker);
            SliceEncoder.EncodeVarUInt62((ulong)(encoder.EncodedByteCount - startPos), sizePlaceholder);
        }
    }

    public sealed partial record class NameTooLong(int MaxLength)
    {
        public const int Discriminant = 1;

        [EditorBrowsable(EditorBrowsableState.Never)]
        internal void Encode(ref SliceEncoder encoder)
        {
            encoder.EncodeVarInt32(Discriminant);
            var sizePlaceholder = encoder.GetPlaceholderSpan(4);
            int startPos = encoder.EncodedByteCount;
            encoder.EncodeInt32(MaxLength);
            encoder.EncodeVarInt32(SliceDefinitions.TagEndMarker);
            SliceEncoder.EncodeVarUInt62((ulong)(encoder.EncodedByteCount - startPos), sizePlaceholder);
        }
    }

    public sealed partial record class Unknown(int Discriminant, ReadOnlyMemory<byte> Fields)
    {
        [EditorBrowsable(EditorBrowsableState.Never)]
        internal void Encode(ref SliceEncoder encoder)
        {
            encoder.EncodeVarInt32(Discriminant);
            encoder.EncodeSize(Fields.Length);
            encoder.WriteByteSpan(Fields.Span);
        }
    }

    public bool Equals(GreeterError other) => Equals(Value, other.Value);

    public override bool Equals(object? obj) => obj is GreeterError other && Equals(other);

    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    public override string ToString() => Value?.ToString() ?? "";

    public static bool operator ==(GreeterError left, GreeterError right) => left.Equals(right);

    public static bool operator !=(GreeterError left, GreeterError right) => !left.Equals(right);
}

internal static class GreeterErrorSliceEncoderExtensions
{
    public static void EncodeGreeterError(this ref SliceEncoder encoder, GreeterError value)
    {
        switch (value)
        {
            case GreeterError.EmptyName emptyName:
                emptyName.Encode(ref encoder);
                break;
            case GreeterError.NameTooLong nameTooLong:
                nameTooLong.Encode(ref encoder);
                break;
            case GreeterError.Unknown unknown:
                unknown.Encode(ref encoder);
                break;
            case null:
                throw new InvalidOperationException("Cannot encode a default GreeterError.");
        }
    }
}

internal static class GreeterErrorSliceDecoderExtensions
{
    public static GreeterError DecodeGreeterError(this ref SliceDecoder decoder)
    {
        return decoder.DecodeVarInt32() switch
        {
            GreeterError.EmptyName.Discriminant => DecodeEmptyName(ref decoder),
            GreeterError.NameTooLong.Discriminant => DecodeNameTooLong(ref decoder),
            int value => new GreeterError.Unknown(value, decoder.DecodeSequence<byte>()),
        };

        static GreeterError.EmptyName DecodeEmptyName(ref SliceDecoder decoder)
        {
            decoder.SkipSize();
            var result = new GreeterError.EmptyName();
            decoder.SkipTagged();
            return result;
        }

        static GreeterError.NameTooLong DecodeNameTooLong(ref SliceDecoder decoder)
        {
            decoder.SkipSize();
            var result = new GreeterError.NameTooLong(MaxLength: decoder.DecodeInt32());
            decoder.SkipTagged();
            return result;
        }
    }
}
