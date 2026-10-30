# Pledge.Testing.TUnit

The TUnit adapter for [Pledge.Testing](https://www.nuget.org/packages/Pledge.Testing): one test
per contracted member of a type, each run as a property test of its
[Pledge](https://www.nuget.org/packages/Pledge) contracts.

> **Alpha.** The API can still change between pre-releases.

```sh
dotnet add package Pledge.Testing.TUnit --prerelease
```

```csharp
using PledgeContracts.Testing.TUnit;

[Test, PledgedMembers(typeof(Account), Iterations = 1_000)]
public Task Account_pledges_hold(PledgeCase pledge) => pledge.CheckAsync();
```

Each member is its own test case, labelled with its signature, such as
`Account.Withdraw(decimal)`. A member whose parameters cannot be generated, such as one taking a
`Span<T>`, is reported as skipped with the reason. Test output goes to the framework's output
for that test.

## Support

Pledge is MIT-licensed and built in the open. If it saves you a bug or two, consider
[sponsoring its development](https://github.com/sponsors/pagebrooks).

[Documentation](https://pledge.pagebrooks.com) · [Source](https://github.com/pagebrooks/pledge) · [Design](https://github.com/pagebrooks/pledge/blob/main/DESIGN.md) · [Issues](https://github.com/pagebrooks/pledge/issues) · [License](https://github.com/pagebrooks/pledge/blob/main/LICENSE)
