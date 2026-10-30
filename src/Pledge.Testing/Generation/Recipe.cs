using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;

namespace PledgeContracts.Testing.Generation;

// A generated value as instructions for building it, not the value itself. Members mutate their receivers and
// arguments, so every probe builds fresh ones, and a counterexample prints the inputs rather than what the
// member left behind.
internal abstract class Recipe
{
    public abstract object? Build();

    public abstract override string ToString();
}

internal sealed class LiteralRecipe(object? value) : Recipe
{
    public override object? Build() => value;

    public override string ToString() => Format(value);

    public static string Format(object? value) => value switch
    {
        null => "null",
        string text => Quote(text),
        char character => "'" + Escape(character.ToString(), '\'') + "'",
        bool flag => flag ? "true" : "false",
        decimal number => number.ToString(CultureInfo.InvariantCulture) + "m",
        float number => number.ToString("R", CultureInfo.InvariantCulture) + "f",
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        long number => number.ToString(CultureInfo.InvariantCulture) + "L",
        Enum member => member.GetType().Name + "." + member,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? value.GetType().Name,
    };

    private static string Quote(string text) => "\"" + Escape(text, '"') + "\"";

    private static string Escape(string text, char quote)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (character == quote || character == '\\')
            {
                builder.Append('\\').Append(character);
            }
            else if (char.IsControl(character) || char.IsSurrogate(character))
            {
                builder.Append(CultureInfo.InvariantCulture, $"\\u{(int)character:x4}");
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}

internal sealed class ConstructorRecipe(ConstructorInfo constructor, IReadOnlyList<Recipe> arguments) : Recipe
{
    public override object? Build()
    {
        var values = arguments.Select(argument => argument.Build()).ToArray();
        try
        {
            return constructor.Invoke(values);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    public override string ToString() => $"new {constructor.DeclaringType!.Name}({string.Join(", ", arguments)})";
}

internal sealed class CollectionRecipe(Type target, Type element, IReadOnlyList<Recipe> items) : Recipe
{
    public override object? Build()
    {
        var array = Array.CreateInstance(element, items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            array.SetValue(items[i].Build(), i);
        }

        return target.IsArray || target.IsAssignableFrom(array.GetType())
            ? array
            : Activator.CreateInstance(typeof(List<>).MakeGenericType(element), array);
    }

    public override string ToString() => "[" + string.Join(", ", items) + "]";
}
