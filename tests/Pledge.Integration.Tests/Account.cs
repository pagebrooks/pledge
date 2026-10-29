namespace PledgeContracts.Integration.Tests;

public sealed class Account(decimal balance)
{
    public decimal Balance { get; private set; } = balance;

    public decimal Withdraw(decimal amount)
    {
        Pledge.Requires(amount > 0);
        Pledge.Requires(amount <= Balance, "insufficient funds");
        Balance -= amount;
        return Balance;
    }

    public void Deposit(decimal amount)
    {
        Pledge.Requires(amount > 0, $"deposits must be positive, got {amount}");
        Balance += amount;
    }

    public bool TryWithdraw(decimal amount, out decimal remaining)
    {
        Pledge.Requires(amount > 0);
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
        return Math.Clamp(value, min, max);
    }
}
