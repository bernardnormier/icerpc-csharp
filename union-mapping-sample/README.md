# Union mapping sample

Hand-written model of the C# code the Slice compiler would generate for variant enums and `Result` under the proposed
C# 15 union mapping, plus a program that round-trips every value through the real `ZeroC.Slice.Codec` encoder and
decoder. Requires .NET SDK 11.0.100-rc.1 or later.

```shell
dotnet run
```

`Result.cs` lives in namespace `Proposal` because the referenced codec still defines the Dunet-based
`ZeroC.Slice.Result`.
