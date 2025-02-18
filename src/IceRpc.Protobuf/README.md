# IceRPC + Protobuf integration

The IceRPC framework allows you to make RPCs with the serialization format and [IDL] of your choice.

The IceRpc.Protobuf assembly is part of the IceRPC + Protobuf integration: it helps
[protoc-gen-icerpc-csharp][protobuf-tools] implement Protobuf services and methods with IceRPC.

[Package][package] | [Source code][source] | [Documentation][docs] | [Examples][examples] | [API reference][api]

## Sample Code

```protobuf
// Protobuf contract

syntax = "proto3";

package visitor_center;

// Represents a simple greeter.
service Greeter {
  rpc Greet (GreetRequest) returns (GreetResponse);
}

message GreetRequest {
  string name = 1;
}

message GreetResponse {
  string greeting = 1;
}

```

```csharp
// Client application

using IceRpc;

// Protobuf module visitor_center in greeter.proto maps to C# namespace VisitorCenter.
using VisitorCenter;

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

// GreeterClient is a struct generated by protoc-gen-icerpc-csharp.
var client = new GreeterClient(connection);
var request = new GreetRequest();
request.Name = Environment.UserName;

GreetResponse response = await client.GreetAsync(request);

Console.WriteLine(response.Greeting);

await connection.ShutdownAsync();
```

```csharp
// Server application

using GreeterProtobufServer;
using IceRpc;

// Create a server that dispatches all requests to the same service, an instance of
// Chatbot.
await using var server = new Server(new Chatbot());
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();

/// <summary>A Chatbot is an IceRPC service that implements Protobuf service 'Greeter'.
/// </summary>
[ProtobufService]
internal partial class Chatbot : IGreeterService
{
    public ValueTask<GreetResponse> GreetAsync(
      GreetRequest message,
      IFeatureCollection? features,
      CancellationToken cancellationToken)
    {
        Console.WriteLine($"Dispatching greet request {{ name = '{message.Name}' }}");
        var response = new GreetResponse();
        response.Greeting = $"Hello, {message.Name}!";
        return new(response);
    }
}
```

[api]: https://docs.testing.zeroc.com/api/csharp/api/IceRpc.Protobuf.html
[docs]: TODO
[IDL]: https://en.wikipedia.org/wiki/Interface_description_language
[examples]: https://github.com/icerpc/icerpc-csharp/tree/main/examples
[package]: https://www.nuget.org/packages/IceRpc.Protobuf
[protobuf-tools]: https://www.nuget.org/packages/IceRpc.Protobuf.Tools
[source]: https://github.com/icerpc/icerpc-csharp/tree/main/src/IceRpc.Protobuf
