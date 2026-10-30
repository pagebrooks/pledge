---
title: Validation or contracts?
description: How to tell input validation from contracts, where each one belongs, and how they work together.
---

# Validation or contracts?

Validation and contracts both check a condition and refuse to go on when it is false. The difference is whose
mistake a failure is.

- **Validation** checks data that comes from outside your code: a form, a request body, a file, a queue
  message, configuration. Bad data is expected. It is one of the normal outcomes, and the code has to handle it
  in every build.
- **A contract** states what correct code guarantees: what a caller must provide, and what a method promises in
  return. A violation is never expected. It means there is a bug, and the fix is a code change.

## How to tell them apart

Ask these questions about the check:

1. **If it fails in production, what gets fixed?** The data, or whoever sent it: validation. The code:
   contract.
2. **Could a correct program fail it?** Yes, because users and other systems send bad data: validation.
   No, only a bug could: contract.
3. **Must it run in every build?** Pledge leaves contracts out of Release builds by default. A check that must
   always run, because something outside your control decides whether it passes, is validation.
4. **Who reads the message?** A user or an API client: validation. A developer reading a log or a failing
   test: contract.

## Side by side

| | Validation | Contracts |
| --- | --- | --- |
| Checks | Data from outside the code | Assumptions inside the code |
| A failure means | Bad input, which is expected | A bug |
| Who acts on it | The user or the sending system fixes the data | A developer fixes the code |
| Reported as | Messages for whoever sent the data, often several at once and often translated | One exception naming the contract, the member and who is to blame |
| Runs | In every build | Chosen per build; by default in Debug and not in Release |
| Typical result | A form error, `400 Bad Request`, a rejected message | A failing test, a crash report, `500 Internal Server Error` |
| .NET tools | DataAnnotations, FluentValidation, ASP.NET Core model validation, options validation | Pledge, `Debug.Assert` |

## Validate at the edges, contract inside

Validate where data enters the system, turn it into values your code can trust, and state those assumptions as
contracts from there on. In an ASP.NET Core endpoint, that looks like this:

```csharp
public sealed record WithdrawRequest(decimal Amount);

public sealed class WithdrawRequestValidator : AbstractValidator<WithdrawRequest>
{
    public WithdrawRequestValidator() =>
        RuleFor(request => request.Amount).GreaterThan(0).WithMessage("Enter an amount greater than zero.");
}

app.MapPost("/accounts/{id}/withdraw", (Guid id, WithdrawRequest request, IValidator<WithdrawRequest> validator, AccountStore accounts) =>
{
    var validation = validator.Validate(request);
    if (!validation.IsValid)
    {
        return Results.ValidationProblem(validation.ToDictionary());
    }

    if (accounts.Find(id) is not { } account)
    {
        return Results.NotFound();
    }

    if (!account.CanWithdraw(request.Amount))
    {
        return Results.Problem("Insufficient funds.", statusCode: StatusCodes.Status409Conflict);
    }

    return Results.Ok(account.Withdraw(request.Amount));
});
```

The domain type does not validate anything. It states what its callers must have checked, and what it
promises in return:

```csharp
public sealed class Account(decimal balance)
{
    public decimal Balance { get; private set; } = balance;

    [Pure]
    public bool CanWithdraw(decimal amount) => amount > 0 && amount <= Balance;

    public decimal Withdraw(decimal amount)
    {
        Pledge.Requires(CanWithdraw(amount), "check CanWithdraw first");
        Pledge.Ensures(Balance == Pledge.Old(Balance) - amount);

        Balance -= amount;
        return Balance;
    }
}
```

If a future endpoint forgets to check, the precondition catches it in Debug and in tests, and blames the
endpoint's line.

## Business rules: the grey area

"Insufficient funds" above is both validation and a contract, depending on where you stand. To the person
asking for the money it is an expected answer, so the endpoint handles it and returns a `409`. To `Withdraw`
it is a precondition: callers are expected to check first, and `CanWithdraw` gives them a way to.

That is the rule for business rules in general:

- **If callers can check beforehand**, make it a precondition, and give them the query method to check with.
- **If they cannot**, because the answer depends on something they do not control, such as stock another
  request may take first, do not make it a precondition. Return the outcome instead, for example
  `bool TryReserve(...)` or a result type, and state what holds in each case with `Ensures`.

## Guard clauses

`ArgumentNullException.ThrowIfNull`, `ArgumentOutOfRangeException.ThrowIfNegative` and libraries such as
Ardalis.GuardClauses are preconditions written by hand. They run in every build and throw
`ArgumentException`. They are still the right tool in two places:

- **The public API of a library.** Pledge checks calls in the project being compiled, so a call from a
  consumer's assembly is not checked.
- **Anywhere a check must run in every build**, whatever the enforcement mode.

Inside your own code, Pledge preconditions add blame, postconditions and generated tests on top. Pledge's
exceptions derive from `BrokenPledgeException`, not `ArgumentException`, so a `catch (ArgumentException)`
written for bad input does not swallow a bug.

## Things to avoid

- **Contracts as the only check on untrusted input.** In Release they are off unless you turn them on.
- **Contract messages shown to users.** They are written for developers, and they name your code.
- **Catching `BrokenPledgeException` to recover.** A violation is a bug to fix. Catch it only at the top
  level, to log it.
- **Validation that throws for every bad field.** Users want all the problems at once, which is what
  validation libraries collect for you.
