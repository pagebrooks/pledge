
namespace PledgeContracts.Testing.Tests.Domain;

// Correct: every contract holds for every input the preconditions accept.
public sealed class Account(decimal balance)
{
    public decimal Balance { get; private set; } = balance;

    public bool Frozen { get; set; }

    public decimal Withdraw(decimal amount)
    {
        Pledge.Requires(amount > 0);
        Pledge.Requires(amount <= Balance, "insufficient funds");
        Pledge.Ensures(Balance == Pledge.Old(Balance) - amount);
        Pledge.Ensures(Pledge.Result<decimal>() >= 0);
        Pledge.EnsuresOnThrow<InvalidOperationException>(Balance == Pledge.Old(Balance));

        if (Frozen)
        {
            throw new InvalidOperationException("The account is frozen.");
        }

        Balance -= amount;
        return Balance;
    }

    public async Task<decimal> DepositAsync(decimal amount, CancellationToken cancellationToken)
    {
        Pledge.Requires(amount > 0);
        Pledge.Requires(amount <= 1_000_000m, "deposits above a million need review");
        Pledge.Ensures(Pledge.Result<decimal>() == Pledge.Old(Balance) + amount);

        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        Balance += amount;
        return Balance;
    }

    public static decimal Fee(decimal amount)
    {
        Pledge.Requires(amount >= 0);
        Pledge.Ensures(Pledge.Result<decimal>() >= 0 && Pledge.Result<decimal>() <= amount);

        return amount / 100m;
    }
}

// The seeded bug: Withdraw rounds the amount to cents before taking it, so the balance does not drop by exactly
// the amount asked for. Every unit test in SeededBugTests passes because they all use whole cents; the property
// test finds it at once, because generated decimals rarely are.
public sealed class RoundingAccount(decimal balance)
{
    public decimal Balance { get; private set; } = balance;

    public decimal Withdraw(decimal amount)
    {
        Pledge.Requires(amount > 0);
        Pledge.Requires(amount <= Balance, "insufficient funds");
        Pledge.Ensures(Balance == Pledge.Old(Balance) - amount);

        Balance -= decimal.Round(amount, 2);
        return Balance;
    }
}
