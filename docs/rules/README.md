# Pledge diagnostics

One page per rule. Each diagnostic links here from the IDE and the build log. Severities are the
defaults; `.editorconfig` can change them (`dotnet_diagnostic.PLEDGE004.severity = error`).

| ID | Severity | Rule | Fix |
| --- | --- | --- | --- |
| [PLEDGE001](PLEDGE001.md) | Error | Contract call is not at the top of the method | Move it to the top |
| [PLEDGE002](PLEDGE002.md) | Error | `Result<T>()` used incorrectly | Use the method's result type |
| [PLEDGE003](PLEDGE003.md) | Error | `Old(...)` used outside a postcondition | |
| [PLEDGE004](PLEDGE004.md) | Warning | Impure expression in a contract | Mark the callee `[Pure]` |
| [PLEDGE005](PLEDGE005.md) | Warning | Pledge generator is not active | |
| [PLEDGE006](PLEDGE006.md) | Info | Contracts are not checked here: no call sites, a delegate, or a call through an interface or base method | Call the contracted method directly |
| [PLEDGE007](PLEDGE007.md) | Warning | `Old(...)` of a mutable reference | Copy with `.ToArray()` or `with { }` |
| [PLEDGE008](PLEDGE008.md) | Warning | Constant argument violates a precondition | |
| [PLEDGE009](PLEDGE009.md) | Warning | Contract on an unsupported member | |

PLEDGE005, PLEDGE009 and PLEDGE006's no-call-sites form come from the source generator, the rest from
analyzers in the same assembly. All ship in the `Pledge` package.
