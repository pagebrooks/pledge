using Microsoft.CodeAnalysis;

namespace Pledge.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class PledgeGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // The pipeline lands with the first authoring-model milestone.
    }
}
