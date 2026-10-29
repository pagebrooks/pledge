using BankAccount;
using PledgeContracts;

var account = new Account(100m);
Console.WriteLine($"Balance after withdrawing 30: {account.Withdraw(30m)}");

try
{
    account.Withdraw(500m);
}
catch (PreconditionViolationException exception)
{
    Console.WriteLine(exception.Message);
}
