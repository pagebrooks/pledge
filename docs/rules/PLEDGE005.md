# PLEDGE005: Pledge generator is not active

**Severity:** Warning.

The generator produced contracts for this project, but the compiler is not configured to accept the
interceptors it emits: the `InterceptorsNamespaces` feature does not include
`PledgeContracts.Generated`. Every contract in the project compiles to a no-op.

Referencing the `Pledge` package normally takes care of this, directly or through a project that
references it. Its `Pledge.targets` (in the package's `buildTransitive` folder) appends the
namespace after your project file has been read, so a project that sets `InterceptorsNamespaces` for its
own generators keeps both, and a build target fails with the exact line to add if an import later in the
build still replaced the property. PLEDGE005 is reached only when those build assets are not imported:

- the package reference has `ExcludeAssets="buildTransitive"`, or a project in between drops the
  package's `buildTransitive` assets with `PrivateAssets`;
- the analyzer assemblies were copied into the project by hand;
- a legacy project format that does not import package build files.

The fix is in the project file, which is why no code fix is offered:

```xml
<PropertyGroup>
  <InterceptorsNamespaces>$(InterceptorsNamespaces);PledgeContracts.Generated</InterceptorsNamespaces>
</PropertyGroup>
```

Or restore the package's build assets so `Pledge.targets` does it for you.
