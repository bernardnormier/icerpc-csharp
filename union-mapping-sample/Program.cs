using System.Buffers;
using System.Runtime.CompilerServices;
using Proposal;
using TwoD;
using VisitorCenter;
using ZeroC.Slice.Codec;

// --- Variant enum: construct, encode, decode, match ---

Shape shape = new Shape.Rectangle(3.0, 4.0);
byte[] bytes = Encode((ref SliceEncoder encoder) => encoder.EncodeShape(shape));
Console.WriteLine($"Rectangle encodes to {bytes.Length} bytes: {Convert.ToHexString(bytes)}");

var decoder = new SliceDecoder(bytes);
Shape decoded = decoder.DecodeShape();

double area = decoded switch
{
    Shape.Circle(var radius) => Math.PI * radius * radius,
    Shape.Rectangle(var width, var height) => width * height,
    Shape.Point => 0.0,
    Shape.Unknown => throw new NotSupportedException(),
};
Console.WriteLine($"Decoded {decoded}, area {area}, equal to original: {decoded == shape}");

// A peer with a newer Slice definition sends discriminant 7 with an unknown payload.
byte[] fromNewerPeer = Encode(
    (ref SliceEncoder encoder) => encoder.EncodeShape(new Shape.Unknown(7, new byte[] { 1, 2, 3 })));
decoder = new SliceDecoder(fromNewerPeer);
Shape unknown = decoder.DecodeShape();
Console.WriteLine(unknown is Shape.Unknown(var discriminant, var fields) ?
    $"Unknown variant {discriminant} with {fields.Length} payload bytes" :
    "expected Unknown");

// Optional field: Shape? is Nullable<Shape>; one null arm covers "no value".
Shape? optional = null;
Console.WriteLine(optional switch
{
    Shape.Circle => "circle",
    Shape.Rectangle => "rectangle",
    Shape.Point => "point",
    Shape.Unknown => "unknown",
    null => "no shape",
});

// default(Shape) has no value; encoding it throws instead of writing garbage.
try
{
    Encode((ref SliceEncoder encoder) => encoder.EncodeShape(default));
}
catch (InvalidOperationException exception)
{
    Console.WriteLine(exception.Message);
}

// --- Result<string, GreeterError> ---

foreach (string name in new[] { "Bernard", "", new string('x', 100) })
{
    Result<string, GreeterError> result = Greet(name);

    byte[] resultBytes = Encode((ref SliceEncoder encoder) => encoder.EncodeResult(
        result,
        (ref SliceEncoder encoder, string value) => encoder.EncodeString(value),
        (ref SliceEncoder encoder, GreeterError value) => encoder.EncodeGreeterError(value)));

    decoder = new SliceDecoder(resultBytes);
    // Static call only because the referenced Codec still has the Dunet-based DecodeResult with the same parameters.
    Result<string, GreeterError> decodedResult = ResultSliceDecoderExtensions.DecodeResult(
        ref decoder,
        (ref SliceDecoder decoder) => decoder.DecodeString(),
        (ref SliceDecoder decoder) => decoder.DecodeGreeterError());

    string message = decodedResult switch
    {
        Success<string>(var greeting) => greeting,
        Failure<GreeterError>(var error) => error switch
        {
            GreeterError.EmptyName => "the name is empty",
            GreeterError.NameTooLong(var maxLength) => $"the name exceeds {maxLength} characters",
            GreeterError.Unknown => "unknown error",
        },
    };
    Console.WriteLine($"{resultBytes.Length,3} bytes -> {message} (round-trip equal: {decodedResult == result})");
}

Console.WriteLine(
    $"sizeof Shape = {Unsafe.SizeOf<Shape>()}, " +
    $"sizeof Result<string, GreeterError> = {Unsafe.SizeOf<Result<string, GreeterError>>()}");

// A service implementation returns the case type; the union conversion applies inside the ValueTask constructor.
static Result<string, GreeterError> Greet(string name) => name.Length switch
{
    0 => new Failure<GreeterError>(new GreeterError.EmptyName()),
    > 50 => new Failure<GreeterError>(new GreeterError.NameTooLong(50)),
    _ => new Success<string>($"Hello, {name}!"),
};

static byte[] Encode(EncodeAction encodeAction)
{
    var bufferWriter = new ArrayBufferWriter<byte>();
    var encoder = new SliceEncoder(bufferWriter);
    encodeAction(ref encoder);
    return bufferWriter.WrittenSpan.ToArray();
}
