using PledgeContracts;

namespace Fixture;

// Every contract shape the generator handles, with call sites it would intercept. The Ignore comparison builds
// this file as it is and again with the Pledge lines and the using removed; the assemblies must not differ.
public sealed class Ledger(decimal balance)
{
    private readonly List<decimal> _entries = [];

    public decimal Balance { get; private set; } = balance;

    public bool Frozen { get; set; }

    public IReadOnlyList<decimal> Entries => _entries;

    public decimal Withdraw(decimal amount)
    {
        Pledge.Requires(amount > 0);
        Pledge.Requires(amount <= Balance, "insufficient funds");
        Pledge.Ensures(Balance == Pledge.Old(Balance) - amount);
        Pledge.Ensures(Pledge.Result<decimal>() >= 0);
        Pledge.EnsuresOnThrow<InvalidOperationException>(Balance == Pledge.Old(Balance));

        if (Frozen)
        {
            throw new InvalidOperationException("ledger is frozen");
        }

        Balance -= amount;
        _entries.Add(-amount);
        return Balance;
    }

    public void Deposit(decimal amount)
    {
        Pledge.Requires(amount > 0, $"deposits must be positive, got {amount}");
        Pledge.Ensures(Balance == Pledge.Old(Balance) + amount);
        Pledge.Ensures(Entries.Count == Pledge.Old(Entries.Count) + 1);

        Balance += amount;
        _entries.Add(amount);
    }

    public async Task<decimal> WithdrawAsync(decimal amount, CancellationToken cancellationToken = default)
    {
        Pledge.Requires(amount > 0);
        Pledge.Ensures(Pledge.Result<decimal>() == Balance);
        Pledge.EnsuresOnThrow<OperationCanceledException>(Balance == Pledge.Old(Balance));

        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        return Withdraw(amount);
    }

    public async ValueTask SettleAsync()
    {
        Pledge.Ensures(Balance >= 0);

        await Task.Yield();
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

        remaining = Withdraw(amount);
        return true;
    }

    public static int Clamp(int value, int min, int max)
    {
        Pledge.Requires(min <= max, "min must not exceed max");
        Pledge.Ensures(Pledge.Result<int>() >= min && Pledge.Result<int>() <= max);

        return Math.Clamp(value, min, max);
    }
}

public static class Bookkeeping
{
    public static async Task<decimal> RunAsync()
    {
        var ledger = new Ledger(100m);
        ledger.Deposit(25m);
        ledger.Withdraw(Ledger.Clamp(40, 0, 30));
        ledger.TryWithdraw(5m, out var remaining);
        await ledger.SettleAsync();
        return await ledger.WithdrawAsync(remaining / 10);
    }
}
