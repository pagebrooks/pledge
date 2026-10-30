using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace PledgeContracts;

// The contracted members of every loaded assembly built with <PledgeRegistry>true</PledgeRegistry> (the default
// unless every contract kind is Ignore). Generated module initializers call Register; the testing bridge reads it.
public static class PledgeRegistry
{
    private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, PledgedMember>> _members = new();

    public static void Register(PledgedMember member)
    {
        ArgumentNullException.ThrowIfNull(member);
        _members.GetOrAdd(member.DeclaringType, static _ => new(StringComparer.Ordinal))[member.Signature] = member;
    }

    // The members declared on the type, by signature. The type's module initializer runs first, since typeof alone
    // does not trigger it and the registrations live there.
    public static IReadOnlyList<PledgedMember> For(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        RuntimeHelpers.RunModuleConstructor(type.Module.ModuleHandle);
        return _members.TryGetValue(type, out var members)
            ? [.. members.Values.OrderBy(static member => member.Signature, StringComparer.Ordinal)]
            : [];
    }

    public static IReadOnlyList<PledgedMember> For(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        foreach (var module in assembly.GetModules())
        {
            RuntimeHelpers.RunModuleConstructor(module.ModuleHandle);
        }

        return [.. _members
            .Where(pair => pair.Key.Assembly == assembly)
            .SelectMany(static pair => pair.Value.Values)
            .OrderBy(static member => member.DeclaringType.FullName, StringComparer.Ordinal)
            .ThenBy(static member => member.Signature, StringComparer.Ordinal)];
    }
}
