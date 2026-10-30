using System.Reflection;
using CsCheck;

namespace PledgeContracts.Testing.Generation;

// Chooses a CsCheck generator for each type a pledged member needs. Overrides from PledgePropertyOptions.Use come
// first; then primitives, strings, dates, Guid, enums and Nullable<T>; then arrays and the common collection
// interfaces; then any type with a public constructor whose parameters can all be generated, nested up to
// MaxDepth deep. A parameter whose nullable annotation allows null gets null about one time in ten.
internal sealed class RecipeGenerators(IReadOnlyDictionary<Type, Gen<Recipe>> overrides)
{
    public const int MaxDepth = 3;
    private const int MaxItems = 8;

    private static readonly Gen<Recipe> _null = Gen.Const<Recipe>(new LiteralRecipe(null));

    private static readonly Dictionary<Type, Gen<Recipe>> _builtIn = new()
    {
        [typeof(bool)] = Literal(Gen.Bool),
        [typeof(sbyte)] = Literal(Gen.SByte),
        [typeof(byte)] = Literal(Gen.Byte),
        [typeof(short)] = Literal(Gen.Short),
        [typeof(ushort)] = Literal(Gen.UShort),
        [typeof(int)] = Literal(Gen.Int),
        [typeof(uint)] = Literal(Gen.UInt),
        [typeof(long)] = Literal(Gen.Long),
        [typeof(ulong)] = Literal(Gen.ULong),
        [typeof(float)] = Literal(Gen.Float),
        [typeof(double)] = Literal(Gen.Double),
        [typeof(decimal)] = Literal(Gen.Decimal),
        [typeof(char)] = Literal(Gen.Char),
        [typeof(string)] = Literal(Gen.String),
        [typeof(Guid)] = Literal(Gen.Guid),
        [typeof(DateTime)] = Literal(Gen.DateTime),
        [typeof(DateTimeOffset)] = Literal(Gen.DateTimeOffset),
        [typeof(DateOnly)] = Literal(Gen.DateOnly),
        [typeof(TimeOnly)] = Literal(Gen.TimeOnly),
        [typeof(TimeSpan)] = Literal(Gen.TimeSpan),
        [typeof(CancellationToken)] = Gen.Const<Recipe>(new LiteralRecipe(CancellationToken.None)),
    };

    private static readonly MethodInfo _enum = typeof(RecipeGenerators).GetMethod(nameof(Enum), BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly NullabilityInfoContext _nullability = new();
    private readonly Dictionary<Type, Gen<Recipe>> _constructed = [];

    public static Gen<Recipe> Literal<T>(Gen<T> generator) => Gen.Select(generator, static value => (Recipe)new LiteralRecipe(value));

    // The generator for one slot, or a description of why there is none.
    public Gen<Recipe> For(Type type, bool allowsNull, int depth = 0)
    {
        var generator = ForType(type, depth);
        return allowsNull && !type.IsValueType ? WithNull(generator) : generator;
    }

    public bool AllowsNull(ParameterInfo parameter) =>
        !parameter.ParameterType.IsValueType && _nullability.Create(parameter).WriteState == NullabilityState.Nullable;

    private Gen<Recipe> ForType(Type type, int depth)
    {
        if (type.IsByRef)
        {
            type = type.GetElementType()!;
        }

        if (overrides.TryGetValue(type, out var custom) || _builtIn.TryGetValue(type, out custom))
        {
            return custom;
        }

        if (type.IsEnum)
        {
            return (Gen<Recipe>)_enum.MakeGenericMethod(type).Invoke(null, null)!;
        }

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return WithNull(ForType(underlying, depth));
        }

        if (ElementType(type) is { } element)
        {
            var items = For(element, allowsNull: false, depth + 1);
            return Gen.Select(items.Array[0, MaxItems], recipes => (Recipe)new CollectionRecipe(type, element, recipes));
        }

        return Constructed(type, depth);
    }

    private Gen<Recipe> Constructed(Type type, int depth)
    {
        if (_constructed.TryGetValue(type, out var known))
        {
            return known;
        }

        if (depth >= MaxDepth)
        {
            throw Unsupported(type, $"it is nested more than {MaxDepth} constructors deep");
        }

        if (type.IsAbstract || type.IsInterface || type.ContainsGenericParameters)
        {
            throw Unsupported(type, "it is abstract, an interface or an open generic type");
        }

        List<string> problems = [];
        foreach (var constructor in type.GetConstructors().OrderByDescending(constructor => constructor.GetParameters().Length))
        {
            try
            {
                var parameters = constructor.GetParameters();
                var arguments = parameters.Select(parameter => For(parameter.ParameterType, AllowsNull(parameter), depth + 1)).ToList();
                var generator = Gen.Select(Sequence(arguments), recipes => (Recipe)new ConstructorRecipe(constructor, recipes));
                _constructed[type] = generator;
                return generator;
            }
            catch (UnsupportedTypeException exception)
            {
                problems.Add(exception.Message);
            }
        }

        if (type.IsValueType)
        {
            return Gen.Const<Recipe>(new LiteralRecipe(Activator.CreateInstance(type)));
        }

        throw Unsupported(type, problems.Count == 0 ? "it has no public constructor" : string.Join("; ", problems));
    }

    // One recipe per generator, shrinking each independently.
    public static Gen<Recipe[]> Sequence(IReadOnlyList<Gen<Recipe>> generators)
    {
        var sequence = Gen.Const<IReadOnlyList<Recipe>>([]);
        foreach (var generator in generators)
        {
            sequence = Gen.Select(sequence, generator, static (recipes, recipe) => (IReadOnlyList<Recipe>)[.. recipes, recipe]);
        }

        return Gen.Select(sequence, static recipes => recipes.ToArray());
    }

    private static Type? ElementType(Type type)
    {
        if (type.IsArray)
        {
            return type.GetArrayRank() == 1 ? type.GetElementType() : null;
        }

        if (!type.IsGenericType || type.GetGenericArguments().Length != 1)
        {
            return null;
        }

        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(List<>)
            || definition == typeof(IEnumerable<>)
            || definition == typeof(IReadOnlyList<>)
            || definition == typeof(IReadOnlyCollection<>)
            || definition == typeof(IList<>)
            || definition == typeof(ICollection<>)
            ? type.GetGenericArguments()[0]
            : null;
    }

    private static Gen<Recipe> WithNull(Gen<Recipe> generator) => Gen.Frequency((1, (IGen<Recipe>)_null), (9, generator));

    private static Gen<Recipe> Enum<T>()
        where T : struct, System.Enum => Literal(Gen.Enum<T>());

    private static UnsupportedTypeException Unsupported(Type type, string reason) =>
        new($"cannot generate {type.Name}: {reason}");
}

internal sealed class UnsupportedTypeException(string message) : Exception(message);
