# PLEDGE006: Contracts are not checked here

**Severity:** Info.

In interceptor mode, Pledge checks a contract at the places the method is called, not inside the method.
The compiler swaps each call that binds to a contracted method for a checked one. Anything that reaches the
method another way runs it without its contracts, and PLEDGE006 points at the places in this compilation where
that happens. It has three forms:

- **No call sites.** Nothing in this compilation calls the method, so its contracts never run here. This is
  expected for a library's public API: callers in other assemblies are not intercepted, since interceptors only
  exist within one compilation.
- **A delegate.** The method is turned into a delegate (`Func<decimal, decimal> f = account.Withdraw;`, or a
  method group passed to `Select`). Calls through the delegate are not intercepted.
- **A call through an interface or a base method.** `IAccount account = ...; account.Withdraw(5)` binds to
  `IAccount.Withdraw`, so it is not intercepted even when the object is an `Account` whose `Withdraw` has
  contracts. The same goes for a call through a base class to a contracted override. Only implementations the
  receiver can actually be are reported.

Reflection and `dynamic` are not intercepted either, and cannot be found at build time.

```csharp
public decimal Total(IAccount account) => account.Withdraw(5m);
//                                                ~~~~~~~~ PLEDGE006: This call binds to 'IAccount.Withdraw'
//                                                         and can run 'Account.Withdraw', whose contracts are
//                                                         checked only on calls that bind to 'Account.Withdraw' itself
```

Where it matters, call the contracted method directly: through the concrete type, or with a lambda instead of a
method group (`amount => account.Withdraw(amount)` is an ordinary call and is checked). Decorator mode (v1.0),
which checks inside the member instead of at the call site, will cover these cases without changes.

The diagnostic is informational because each of these is legitimate code. To silence it for a project, add
`dotnet_diagnostic.PLEDGE006.severity = none` to `.editorconfig`. When every kind of contract is `Ignore`,
nothing is checked anywhere and PLEDGE006 is not reported.
