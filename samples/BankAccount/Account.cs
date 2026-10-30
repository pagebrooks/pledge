using PledgeContracts;

namespace BankAccount;

public sealed class Account(decimal balance)
{
    public decimal Balance { get; private set; } = balance;

    public decimal Withdraw(decimal amount)
    {
        Pledge.Requires(amount > 0);
        Pledge.Requires(amount <= Balance, "insufficient funds");
        Pledge.Ensures(Balance == Pledge.Old(Balance) - amount);
        Pledge.Ensures(Pledge.Result<decimal>() >= 0);
        Pledge.EnsuresOnThrow<InvalidOperationException>(Balance == Pledge.Old(Balance));

        Balance -= amount;
        return Balance;
    }
}
