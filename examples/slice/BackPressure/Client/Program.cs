// Copyright (c) ZeroC, Inc.

using BackPressure;
using IceRpc;

await using var connection = new ClientConnection(new Uri("ice://localhost"));

var dataStore = new DataStoreProxy(connection, new Uri("ice:/datastore"));

Task sleepTask = dataStore.SleepAsync(seconds: 20);

var data = new byte[10 * 1024];

for (int i = 0; i < 200; ++i)
{
    await dataStore.UploadAsync(data);
    Console.WriteLine($"Upload #{i}");
}

Console.WriteLine("Waiting for sleep task to complete");
await sleepTask;

await connection.ShutdownAsync();
