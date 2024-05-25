// Copyright (c) ZeroC, Inc.

using BackPressureServer;
using IceRpc;

var serverOptions = new ServerOptions
{
    ConnectionOptions = new ConnectionOptions
    {
        Dispatcher = new Database(),
        MaxDispatches = 1,
        IceIdleTimeout = TimeSpan.FromSeconds(5)
    },
    ServerAddress = new ServerAddress(new Uri("ice://127.0.0.1"))
};

await using var server = new Server(serverOptions);
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
