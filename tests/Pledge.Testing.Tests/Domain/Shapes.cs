
namespace PledgeContracts.Testing.Tests.Domain;

public enum Currency
{
    Usd,
    Eur,
    Gbp,
}

public sealed record Money(decimal Amount, Currency Currency);

public sealed record Transfer(Money Money, string? Memo);

// Members whose inputs exercise the generators: records nested in records, enums, nullable annotations,
// collections, Nullable<T> and a Span the probe cannot box.
public sealed class Ledger
{
    public int Count { get; private set; }

    public Money Record(Transfer transfer)
    {
        Pledge.Requires(transfer.Memo is null || transfer.Memo.Length <= 200);
        Pledge.Ensures(Count == Pledge.Old(Count) + 1);
        Pledge.Ensures(Pledge.Result<Money>() == transfer.Money);

        Count++;
        return transfer.Money;
    }

    public static int Total(IReadOnlyList<int> amounts)
    {
        Pledge.Requires(amounts.All(amount => amount is >= 0 and <= 1_000));
        Pledge.Ensures(Pledge.Result<int>() >= 0);

        return amounts.Sum();
    }

    public static int? Clamp(int? value, List<string> labels)
    {
        Pledge.Requires(labels.Count < 5);
        Pledge.Ensures(Pledge.Result<int?>() is null || Pledge.Result<int?>() >= 0);

        return value is null ? null : Math.Max(0, value.Value);
    }

    public static int First(ReadOnlySpan<int> values)
    {
        Pledge.Requires(values.Length > 0);

        return values[0];
    }
}

public interface IClock
{
    DateTime Now { get; }
}

// Exceptions and inputs the bridge has to handle deliberately.
public static class Edges
{
    // Throws an exception no EnsuresOnThrow declares.
    public static int Parse(string text)
    {
        Pledge.Requires(text.Length < 100);

        return int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
    }

    // A precondition almost no generated int meets.
    public static int Exactly(int value)
    {
        Pledge.Requires(value == 424_242);
        Pledge.Ensures(Pledge.Result<int>() == 424_242);

        return value;
    }

    // An interface parameter the built-in generators cannot construct.
    public static DateTime Stamp(IClock clock)
    {
        Pledge.Requires(clock.Now.Year > 1);

        return clock.Now;
    }
}
