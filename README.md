# IceRPC Slice files

This directory provides [Slice][slice] definitions shared by all IceRPC implementations.

| Subdirectory       | Description                                                                        |
|--------------------|------------------------------------------------------------------------------------|
| Ice                | Interfaces and exceptions provided for interop with [Ice][zeroc-ice] applications. |
| IceRpc             | Types provided by all IceRPC implementations. These types are IceRPC-specific.     |
| WellKnownTypes     | Custom types such as Uri and TimeStamp. These well-known types are RPC-independent.|
| IceRpc/**/Internal | Enumerations and compact structs used by IceRPC implementations to implement the [ice][ice-protocol] protocol, the [icerpc][icerpc-protocol] protocol, the Slice [encoding][slice-encoding] and the [Slic][slic] protocol. These are internal IceRPC implementation details—applications built with IceRPC don't need to see these definitions.|

The copy of record for these Slice files is the [icerpc-slice][icerpc-slice] repository. Each IceRPC implementation is
expected to create its own read-only clone of these Slice files with `git subtree`. For example, the `slice` directory
of the [icerpc-csharp][icerpc-csharp] repository is a git subtree clone of icerpc-slice.

[ice-protocol]: https://docs.testing.zeroc.com/docs/icerpc-core/ice-protocol/protocol-frames
[icerpc-csharp]: https://github.com/icerpc/icerpc-csharp/
[icerpc-protocol]: https://docs.testing.zeroc.com/docs/icerpc-core/icerpc-protocol/mapping-rpcs-to-streams
[icerpc-slice]: https://github.com/icerpc/icerpc-slice
[slic]: https://docs.testing.zeroc.com/icerpc-core/slic-protocol
[slice]: https://docs.testing.zeroc.com/docs/slice
[slice-encoding]: https://docs.testing.zeroc.com/docs/slice/encoding
[zeroc-ice]: https://github.com/zeroc-ice/ice
