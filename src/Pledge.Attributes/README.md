# Pledge.Attributes

The contract metadata attributes of [Pledge](https://www.nuget.org/packages/Pledge), on
`netstandard2.0` with no dependencies: `[Requires]`, `[Ensures]`, `[EnsuresOnThrow]` and
`[Pure]`.

You rarely reference this package yourself. `Pledge` depends on it, and its generator records each
contract as one of these attributes on the assembly, so tools and documentation generators can read
what a member promises without running anything. Reference it directly when a library targets an
older framework and only needs to carry the metadata, or when a tool reads it.

```sh
dotnet add package Pledge.Attributes --prerelease
```

`[Pure]` marks a method as safe to call from a contract expression, which the PLEDGE004 analyzer
otherwise flags.

> **Alpha.** The API can still change between pre-releases.

## Support

Pledge is MIT-licensed and built in the open. If it saves you a bug or two, consider
[sponsoring its development](https://github.com/sponsors/pagebrooks).

[Documentation](https://pledge.pagebrooks.com) · [Source](https://github.com/pagebrooks/pledge) · [Design](https://github.com/pagebrooks/pledge/blob/main/DESIGN.md) · [Issues](https://github.com/pagebrooks/pledge/issues) · [License](https://github.com/pagebrooks/pledge/blob/main/LICENSE)
