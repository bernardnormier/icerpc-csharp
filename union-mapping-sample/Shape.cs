// What the Slice compiler would generate for:
//
// module TwoD
// unchecked enum Shape {
//     Circle(radius: float64)
//     Rectangle(width: float64, height: float64)
//     Point
// }

using System.ComponentModel;
using ZeroC.Slice.Codec;

namespace TwoD;

internal partial union Shape(Shape.Circle, Shape.Rectangle, Shape.Point, Shape.Unknown) : IEquatable<Shape>
{
    public sealed partial record class Circle(double Radius)
    {
        public const int Discriminant = 0;

        [EditorBrowsable(EditorBrowsableState.Never)]
        internal void Encode(ref SliceEncoder encoder)
        {
            encoder.EncodeVarInt32(Discriminant);
            var sizePlaceholder = encoder.GetPlaceholderSpan(4);
            int startPos = encoder.EncodedByteCount;
            encoder.EncodeFloat64(Radius);
            encoder.EncodeVarInt32(SliceDefinitions.TagEndMarker);
            SliceEncoder.EncodeVarUInt62((ulong)(encoder.EncodedByteCount - startPos), sizePlaceholder);
        }
    }

    public sealed partial record class Rectangle(double Width, double Height)
    {
        public const int Discriminant = 1;

        [EditorBrowsable(EditorBrowsableState.Never)]
        internal void Encode(ref SliceEncoder encoder)
        {
            encoder.EncodeVarInt32(Discriminant);
            var sizePlaceholder = encoder.GetPlaceholderSpan(4);
            int startPos = encoder.EncodedByteCount;
            encoder.EncodeFloat64(Width);
            encoder.EncodeFloat64(Height);
            encoder.EncodeVarInt32(SliceDefinitions.TagEndMarker);
            SliceEncoder.EncodeVarUInt62((ulong)(encoder.EncodedByteCount - startPos), sizePlaceholder);
        }
    }

    public sealed partial record class Point
    {
        public const int Discriminant = 2;

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

    /// <summary>Represents a variant not defined in the local Slice definition of unchecked enum 'Shape'.</summary>
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

    public bool Equals(Shape other) => Equals(Value, other.Value);

    public override bool Equals(object? obj) => obj is Shape other && Equals(other);

    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    public override string ToString() => Value?.ToString() ?? "";

    public static bool operator ==(Shape left, Shape right) => left.Equals(right);

    public static bool operator !=(Shape left, Shape right) => !left.Equals(right);
}

internal static class ShapeSliceEncoderExtensions
{
    public static void EncodeShape(this ref SliceEncoder encoder, Shape value)
    {
        switch (value)
        {
            case Shape.Circle circle:
                circle.Encode(ref encoder);
                break;
            case Shape.Rectangle rectangle:
                rectangle.Encode(ref encoder);
                break;
            case Shape.Point point:
                point.Encode(ref encoder);
                break;
            case Shape.Unknown unknown:
                unknown.Encode(ref encoder);
                break;
            case null:
                throw new InvalidOperationException("Cannot encode a default Shape.");
        }
    }
}

internal static class ShapeSliceDecoderExtensions
{
    public static Shape DecodeShape(this ref SliceDecoder decoder)
    {
        return decoder.DecodeVarInt32() switch
        {
            Shape.Circle.Discriminant => DecodeCircle(ref decoder),
            Shape.Rectangle.Discriminant => DecodeRectangle(ref decoder),
            Shape.Point.Discriminant => DecodePoint(ref decoder),
            int value => new Shape.Unknown(value, decoder.DecodeSequence<byte>()),
        };

        static Shape.Circle DecodeCircle(ref SliceDecoder decoder)
        {
            decoder.SkipSize();
            var result = new Shape.Circle(Radius: decoder.DecodeFloat64());
            decoder.SkipTagged();
            return result;
        }

        static Shape.Rectangle DecodeRectangle(ref SliceDecoder decoder)
        {
            decoder.SkipSize();
            var result = new Shape.Rectangle(
                Width: decoder.DecodeFloat64(),
                Height: decoder.DecodeFloat64());
            decoder.SkipTagged();
            return result;
        }

        static Shape.Point DecodePoint(ref SliceDecoder decoder)
        {
            decoder.SkipSize();
            var result = new Shape.Point();
            decoder.SkipTagged();
            return result;
        }
    }
}
