using Xunit;
using Xunit.Sdk;

namespace PledgeContracts.Testing.Xunit;

// One pledged member as a theory argument. It serializes as its type and signature, so test explorers list each
// member at discovery and can run one alone.
public sealed class PledgeCase : IXunitSerializable
{
    private PledgedMember? _member;

    [Obsolete("Called by xUnit when it deserializes a test case; use PledgedMembersAttribute instead.")]
    public PledgeCase()
    {
    }

    internal PledgeCase(PledgedMember member, int iterations, string? seed)
    {
        _member = member;
        Iterations = iterations;
        Seed = seed;
    }

    public PledgedMember Member => _member ?? throw new InvalidOperationException("This PledgeCase was not initialized by xUnit or PledgedMembersAttribute.");

    public int Iterations { get; private set; }

    public string? Seed { get; private set; }

    // Runs the property test with the attribute's iterations and seed, writing the summary to the test's output.
    public Task CheckAsync() => CheckAsync(new PledgePropertyOptions { Iterations = Iterations, Seed = Seed });

    // Runs it with these options instead; Output defaults to the test's output when it is not set.
    public Task CheckAsync(PledgePropertyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Output ??= line => TestContext.Current.TestOutputHelper?.WriteLine(line);
        return PledgeProperties.CheckAsync(Member, options);
    }

    public override string ToString() => _member?.ToString() ?? "(uninitialized)";

    void IXunitSerializable.Deserialize(IXunitSerializationInfo info)
    {
        var typeName = (string)info.GetValue("type")!;
        var signature = (string)info.GetValue("signature")!;
        var type = Type.GetType(typeName, throwOnError: true)!;
        _member = PledgeRegistry.For(type).FirstOrDefault(member => member.Signature == signature)
            ?? throw new InvalidOperationException($"{type.FullName} no longer has a pledged member {signature}.");
        Iterations = (int)info.GetValue("iterations")!;
        Seed = (string?)info.GetValue("seed");
    }

    void IXunitSerializable.Serialize(IXunitSerializationInfo info)
    {
        info.AddValue("type", Member.DeclaringType.AssemblyQualifiedName, typeof(string));
        info.AddValue("signature", Member.Signature, typeof(string));
        info.AddValue("iterations", Iterations, typeof(int));
        info.AddValue("seed", Seed, typeof(string));
    }
}
