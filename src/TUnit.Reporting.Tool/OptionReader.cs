namespace TUnit.Reporting.Tool;

internal static class OptionReader
{
    internal static string RequireValue(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
        {
            throw new ArgumentException($"option '{args[i]}' requires a value");
        }
        return args[++i];
    }

    internal static int RequirePositiveInt(string[] args, ref int i)
    {
        var option = args[i];
        var value = RequireValue(args, ref i);
        if (!int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed) || parsed < 1)
        {
            throw new ArgumentException($"option '{option}' expects a positive whole number, got '{value}'");
        }
        return parsed;
    }
}
