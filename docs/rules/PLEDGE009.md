# PLEDGE009: Contract on an unsupported member

**Severity:** Warning.

The generator found contracts it cannot turn into checks yet, so they compile to no-ops. The message
names the member and the reason. Current reasons:

- the member is not an ordinary method: constructors, operators, property and indexer accessors (which
  are not invocations and cannot be intercepted; properties come with decorator mode in v1.0), local
  functions, lambdas, top-level statements, expression-bodied methods;
- the method or its type is generic, the type is a struct or interface, or the method is an extension
  method;
- the method returns by `ref`, or is `async void` with postconditions;
- the method or its containing type is private, protected or private protected, so the generated check
  outside the type cannot call it;
- the contract expression uses `base`, a local function, or a private or protected member.

Preconditions are meant to be satisfied by callers, so a contract that only a private member can
express is usually a sign it should be an assertion in the body instead.
