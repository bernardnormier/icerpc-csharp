// Copyright (c) ZeroC, Inc.

using BackPressure;
using IceRpc.Features;
using IceRpc.Slice;

namespace BackPressureServer;

[SliceService]
internal partial class Database : IDataStoreService
{
    public ValueTask UploadAsync(byte[] data, IFeatureCollection features, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Dispatching upload request with {data.Length} bytes");
        return default;
    }

    public async ValueTask SleepAsync(int seconds, IFeatureCollection features, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Dispatching sleep request for {seconds} seconds");
        await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
        Console.WriteLine("Sleep request completed");
    }
}
