// The proposed Result for ZeroC.Slice.Codec. It lives in namespace Proposal here only because the referenced Codec
// still defines the Dunet-based ZeroC.Slice.Result.

using System.Runtime.CompilerServices;
using ZeroC.Slice.Codec;

namespace Proposal;

/// <summary>The success case of a <see cref="Result{TSuccess, TFailure}" />.</summary>
public readonly record struct Success<T>(T Value);

/// <summary>The failure case of a <see cref="Result{TSuccess, TFailure}" />.</summary>
public readonly record struct Failure<T>(T Value);

/// <summary>A union that holds either a <see cref="Success{T}" /> or a <see cref="Failure{T}" />. The Slice Result
/// type maps to this struct. Use pattern matching to access the value; <see cref="HasValue" /> and
/// <see cref="TryGetValue(out Success{TSuccess})" /> exist for the compiler.</summary>
[Union]
public readonly partial record struct Result<TSuccess, TFailure> : IUnion
{
    private enum Kind : byte
    {
        None,
        Success,
        Failure,
    }

    private readonly TSuccess _success;
    private readonly TFailure _failure;
    private readonly Kind _kind;

    public Result(Success<TSuccess> value)
    {
        _success = value.Value;
        _failure = default!;
        _kind = Kind.Success;
    }

    public Result(Failure<TFailure> value)
    {
        _success = default!;
        _failure = value.Value;
        _kind = Kind.Failure;
    }

    public object? Value => _kind switch
    {
        Kind.Success => new Success<TSuccess>(_success),
        Kind.Failure => new Failure<TFailure>(_failure),
        _ => null,
    };

    public bool HasValue => _kind != Kind.None;

    public bool TryGetValue(out Success<TSuccess> value)
    {
        value = new(_success);
        return _kind == Kind.Success;
    }

    public bool TryGetValue(out Failure<TFailure> value)
    {
        value = new(_failure);
        return _kind == Kind.Failure;
    }

    public override string ToString() => Value?.ToString() ?? "";
}

public static class ResultSliceEncoderExtensions
{
    public static void EncodeResult<TSuccess, TFailure>(
        this ref SliceEncoder encoder,
        Result<TSuccess, TFailure> value,
        EncodeAction<TSuccess> successEncodeAction,
        EncodeAction<TFailure> failureEncodeAction)
    {
        if (value.TryGetValue(out Success<TSuccess> success))
        {
            encoder.EncodeVarInt32(0);
            successEncodeAction(ref encoder, success.Value);
        }
        else if (value.TryGetValue(out Failure<TFailure> failure))
        {
            encoder.EncodeVarInt32(1);
            failureEncodeAction(ref encoder, failure.Value);
        }
        else
        {
            throw new InvalidOperationException("Cannot encode a default Result.");
        }
    }
}

public static class ResultSliceDecoderExtensions
{
    public static Result<TSuccess, TFailure> DecodeResult<TSuccess, TFailure>(
        this ref SliceDecoder decoder,
        DecodeFunc<TSuccess> successDecodeFunc,
        DecodeFunc<TFailure> failureDecodeFunc) =>
        decoder.DecodeVarInt32() switch
        {
            0 => new Success<TSuccess>(successDecodeFunc(ref decoder)),
            1 => new Failure<TFailure>(failureDecodeFunc(ref decoder)),
            int value => throw new InvalidDataException($"Received invalid discriminant value '{value}' for Result."),
        };
}
