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
public readonly partial struct Result<TSuccess, TFailure> : IUnion, IEquatable<Result<TSuccess, TFailure>>
{
    private readonly TSuccess _success;
    private readonly TFailure _failure;
    private readonly byte _tag; // 0 = default (no value), 1 = success, 2 = failure

    public Result(Success<TSuccess> value)
    {
        _success = value.Value;
        _failure = default!;
        _tag = 1;
    }

    public Result(Failure<TFailure> value)
    {
        _success = default!;
        _failure = value.Value;
        _tag = 2;
    }

    public object? Value => _tag switch
    {
        1 => new Success<TSuccess>(_success),
        2 => new Failure<TFailure>(_failure),
        _ => null,
    };

    public bool HasValue => _tag != 0;

    public bool TryGetValue(out Success<TSuccess> value)
    {
        value = new(_success);
        return _tag == 1;
    }

    public bool TryGetValue(out Failure<TFailure> value)
    {
        value = new(_failure);
        return _tag == 2;
    }

    public bool Equals(Result<TSuccess, TFailure> other) =>
        _tag == other._tag && _tag switch
        {
            1 => EqualityComparer<TSuccess>.Default.Equals(_success, other._success),
            2 => EqualityComparer<TFailure>.Default.Equals(_failure, other._failure),
            _ => true,
        };

    public override bool Equals(object? obj) => obj is Result<TSuccess, TFailure> other && Equals(other);

    public override int GetHashCode() => _tag switch
    {
        1 => HashCode.Combine(_tag, _success),
        2 => HashCode.Combine(_tag, _failure),
        _ => 0,
    };

    public override string ToString() => Value?.ToString() ?? "";

    public static bool operator ==(Result<TSuccess, TFailure> left, Result<TSuccess, TFailure> right) =>
        left.Equals(right);

    public static bool operator !=(Result<TSuccess, TFailure> left, Result<TSuccess, TFailure> right) =>
        !left.Equals(right);
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
