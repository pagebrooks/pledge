using PledgeContracts;

namespace Fixture;

// Leaky double-subtracts, which breaks the Withdraw postcondition.
public sealed class Account(decimal balance)
{
    public decimal Balance { get; private set; } = balance;

    public bool Leaky { get; set; }

    public decimal Deposit(decimal amount)
    {
        Pledge.Requires(amount > 0);
        Pledge.Ensures(Balance == Pledge.Old(Balance) + amount);

        Balance += amount;
        return Balance;
    }

    public decimal Withdraw(decimal amount)
    {
        Pledge.Requires(amount > 0);
        Pledge.Ensures(Balance == Pledge.Old(Balance) - amount);

        Balance -= Leaky ? amount * 2 : amount;
        return Balance;
    }
}
