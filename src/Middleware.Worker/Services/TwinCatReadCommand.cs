namespace Middleware.Worker.Services;

public sealed record TwinCatReadCommand(string GroupName, IReadOnlyCollection<string> Keys)
{
    public static TwinCatReadCommand? TryParse(string[] arguments)
    {
        if (arguments.Length == 0 || !arguments[0].Equals("--twincat-read", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (arguments.Length < 2 || arguments[1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Usage: dotnet run --project .\\src\\Middleware.Worker -- --twincat-read <group> [key1,key2,...]");
        }

        var keys = arguments.Length < 3
            ? Array.Empty<string>()
            : arguments[2].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new TwinCatReadCommand(arguments[1], keys);
    }
}
