namespace PledgeContracts.Integration.Tests;

public sealed class Account(decimal balance)
{
    public decimal Balance { get; private set; } = balance;

    // Simulated bugs the tests switch on: Leaky double-subtracts, Corrupting mutates before throwing.
    public bool Frozen { get; set; }

    public bool Leaky { get; set; }

    public bool Corrupting { get; set; }

    public decimal Withdraw(decimal amount)
    {
        Pledge.Requires(amount > 0);
        Pledge.Requires(amount <= Balance, "insufficient funds");
        Pledge.Ensures(Balance == Pledge.Old(Balance) - amount);
        Pledge.Ensures(Pledge.Result<decimal>() >= 0);
        Pledge.EnsuresOnThrow<InvalidOperationException>(Balance == Pledge.Old(Balance));

        if (Frozen)
        {
            if (Corrupting)
            {
                Balance = -1m;
            }

            throw new InvalidOperationException("account is frozen");
        }

        Balance -= Leaky ? amount * 2 : amount;
        return Balance;
    }

    public async Task<decimal> WithdrawAsync(decimal amount, CancellationToken cancellationToken = default)
    {
        Pledge.Requires(amount > 0);
        Pledge.Ensures(Balance == Pledge.Old(Balance) - amount);
        Pledge.Ensures(Pledge.Result<decimal>() >= 0);

        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        Balance -= Leaky ? amount * 2 : amount;
        return Balance;
    }

    public void Deposit(decimal amount)
    {
        Pledge.Requires(amount > 0, $"deposits must be positive, got {amount}");
        Pledge.Ensures(Balance == Pledge.Old(Balance) + amount);
        Balance += amount;
    }

    public bool TryWithdraw(decimal amount, out decimal remaining)
    {
        Pledge.Requires(amount > 0);
        Pledge.Ensures(remaining == Balance);
        if (amount > Balance)
        {
            remaining = Balance;
            return false;
        }

        Balance -= amount;
        remaining = Balance;
        return true;
    }

    public static int Clamp(int value, int min, int max)
    {
        Pledge.Requires(min <= max);
        Pledge.Ensures(Pledge.Result<int>() >= min && Pledge.Result<int>() <= max);
        return Math.Clamp(value, min, max);
    }
}
