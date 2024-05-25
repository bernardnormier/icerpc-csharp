# BackPressure

The BackPressure example illustrates how to a client gets "back pressured" by the server
when the server no longer reads requests off the network.

You can build the client and server applications with:

``` shell
dotnet build
```

First start the Server program:

```shell
cd Server
dotnet run
```

In a separate terminal, start the Client program:

```shell
cd Client
dotnet run
```
