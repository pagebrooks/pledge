using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace PledgeContracts;

public static class Pledge
{
    // No build defines this symbol, so every call to a [Conditional] member below is removed by the compiler,
    // arguments included. The generator reads the calls from syntax and emits the checks into a wrapper.
    internal const string NeverDefined = "PLEDGE_NEVER";

    [Conditional(NeverDefined)]
    public static void Requires(bool condition, [CallerArgumentExpression(nameof(condition))] string? message = null)
    {
    }

    [Conditional(NeverDefined)]
    public static void Ensures(bool condition, [CallerArgumentExpression(nameof(condition))] string? message = null)
    {
    }

    [Conditional(NeverDefined)]
    public static void EnsuresOnThrow<TException>(bool condition, [CallerArgumentExpression(nameof(condition))] string? message = null)
        where TException : Exception
    {
    }

    // Result and Old return values, so they cannot be [Conditional]. They are only legal inside a contract call,
    // which is itself removed, so a call that reaches this body is outside a contract (DBC002 catches it at build).
    public static T Result<T>() =>
        throw new InvalidOperationException("Pledge.Result<T>() stands for the return value inside Pledge.Ensures and has no meaning elsewhere.");

    public static T Old<T>(T expression) =>
        throw new InvalidOperationException("Pledge.Old(...) stands for a value at method entry inside Pledge.Ensures and has no meaning elsewhere.");
}
