# Deadline

The Deadline example illustrates how to use the deadline interceptor to add an invocation deadline and shows
how invocations that exceed the deadline fail with TimeoutException. It also demonstrates how the IDeadlineFeature
can be used to set the deadline for an invocation.

You can build the client and server applications with:

``` shell
dotnet build
```

First start the Server program:

```shell
cd Server
dotnet run
```

In a separate window, start the Client program:

```shell
cd Client
dotnet run
```
