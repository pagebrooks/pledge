using System.Reflection;

namespace PledgeContracts.Tests;

public sealed class AttributeTests
{
    [Theory]
    [InlineData(typeof(RequiresAttribute))]
    [InlineData(typeof(EnsuresAttribute))]
    [InlineData(typeof(EnsuresOnThrowAttribute))]
    public void Metadata_attributes_stack_on_members_and_are_not_inherited(Type attribute)
    {
        var usage = attribute.GetCustomAttribute<AttributeUsageAttribute>();

        Assert.NotNull(usage);
        Assert.True(usage.AllowMultiple);
        Assert.False(usage.Inherited);
        Assert.Equal(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Constructor, usage.ValidOn);
        Assert.True(attribute.IsSubclassOf(typeof(ContractMetadataAttribute)));
    }

    [Fact]
    public void Metadata_attributes_carry_the_expression_text()
    {
        Assert.Equal("amount > 0", new RequiresAttribute("amount > 0").Expression);
        Assert.Equal("Result >= 0", new EnsuresAttribute("Result >= 0").Expression);

        var onThrow = new EnsuresOnThrowAttribute(typeof(InvalidOperationException), "Balance == Old(Balance)");
        Assert.Equal(typeof(InvalidOperationException), onThrow.ExceptionType);
        Assert.Equal("Balance == Old(Balance)", onThrow.Expression);
    }

    [Fact]
    public void Metadata_attributes_reject_null_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => new RequiresAttribute(null!));
        Assert.Throws<ArgumentNullException>(() => new EnsuresOnThrowAttribute(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => new EnsuresOnThrowAttribute(typeof(Exception), null!));
    }

    [Fact]
    public void Pure_mirrors_the_BCL_attribute()
    {
        var usage = typeof(PureAttribute).GetCustomAttribute<AttributeUsageAttribute>();
        var bcl = typeof(System.Diagnostics.Contracts.PureAttribute).GetCustomAttribute<AttributeUsageAttribute>();

        Assert.NotNull(usage);
        Assert.NotNull(bcl);
        Assert.Equal(bcl.ValidOn, usage.ValidOn);
        Assert.Equal(bcl.AllowMultiple, usage.AllowMultiple);
        Assert.Equal(bcl.Inherited, usage.Inherited);
    }
}
