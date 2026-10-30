# PLEDGE008: Constant argument violates a precondition

**Severity:** Warning.

A call passes constants that a `Pledge.Requires` of the callee rejects. The precondition would throw on
every run, so the bug is visible at compile time.

```csharp
account.Withdraw(-5m);   // PLEDGE008: Argument amount: -5 violates the precondition 'amount > 0' of 'Account.Withdraw'
```

The analyzer substitutes constant arguments (including omitted optional parameters) into each
precondition and evaluates it with three-valued logic. It understands literals, comparisons, `&&`, `||`,
`!`, `??`, the conditional operator, `is` with constant, relational, `and`/`or`/`not` patterns,
`string.Length`, `string.IsNullOrEmpty` and `string.IsNullOrWhiteSpace`. Anything else, including a
member read such as `Balance`, is unknown, and a precondition whose outcome depends on an unknown is
not reported. Only preconditions of methods declared in the same compilation are read; reading them
from referenced assemblies' metadata attributes comes in v1.0.

Tests that break a contract on purpose can disable the rule for the file:

```csharp
#pragma warning disable PLEDGE008
```
