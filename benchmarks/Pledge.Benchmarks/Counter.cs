namespace PledgeContracts.Benchmarks;

// Each contracted member has a twin with the same check written by hand, the way a guard clause is written today,
// which is the baseline the performance budget compares against. Every call site in this assembly is intercepted, so calling Add runs Pledge's checks.
public sealed class Counter
{
    public int Total { get; private set; }

    public void Add(int amount)
    {
        Pledge.Requires(amount > 0);

        Total += amount;
    }

    public void AddHandWritten(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        Total += amount;
    }

    public void AddChecked(int amount)
    {
        Pledge.Requires(amount > 0);
        Pledge.Ensures(Total == Pledge.Old(Total) + amount);

        Total += amount;
    }

    public void AddCheckedHandWritten(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        var old = Total;
        Total += amount;
        if (Total != old + amount)
        {
            throw new InvalidOperationException("Total == Old(Total) + amount");
        }
    }

    public async Task<int> AddAsync(int amount)
    {
        Pledge.Requires(amount > 0);
        Pledge.Ensures(Pledge.Result<int>() == Total);

        await Task.CompletedTask;
        Total += amount;
        return Total;
    }

    // The hand-written equivalent of Pledge's async wrapper: check, await the real body, check the result.
    public Task<int> AddAsyncHandWritten(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        return Awaited(amount);

        async Task<int> Awaited(int amount)
        {
            var result = await AddAsyncBody(amount);
            if (result != Total)
            {
                throw new InvalidOperationException("Result<int>() == Total");
            }

            return result;
        }
    }

    private async Task<int> AddAsyncBody(int amount)
    {
        await Task.CompletedTask;
        Total += amount;
        return Total;
    }
}
