using PledgeContracts;

namespace BankAccount;

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
}
