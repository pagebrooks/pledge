using Fixture;
using PledgeContracts;

// Prints one line per event so the tests can tell what each mode did: the handler seeing a violation, a call
// returning (and with what), a call throwing (and what). EnforceFailFast never reaches "done". With the argument
// "registry" it prints instead which members registered a testing probe.
if (args is ["registry"])
{
    Console.WriteLine($"registry: {string.Join(", ", PledgeRegistry.For(typeof(Account)))}");
    return;
}

PledgeRuntime.ViolationHandler = violation => Console.WriteLine($"handler: {violation.Kind} {violation.Expression}");

var account = new Account(10m);
var negative = -1m;

Run("deposit", () => account.Deposit(negative));
Run("withdraw", () =>
{
    account.Leaky = true;
    return account.Withdraw(2m);
});
Console.WriteLine("done");

static void Run(string label, Func<decimal> action)
{
    try
    {
        Console.WriteLine($"{label}: returned {action()}");
    }
    catch (BrokenPledgeException exception)
    {
        Console.WriteLine($"{label}: threw {exception.GetType().Name}");
    }
}
