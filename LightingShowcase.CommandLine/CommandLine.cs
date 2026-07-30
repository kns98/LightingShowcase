namespace LightingShowcase.CommandLine;

internal sealed class CommandLine
{
    private static readonly HashSet<string> FlagOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "help",
        "no-shadows"
    };

    private readonly Dictionary<string, string> _values;
    private readonly HashSet<string> _switches;

    private CommandLine(Dictionary<string, string> values, HashSet<string> switches)
    {
        _values = values;
        _switches = switches;
    }

    public IReadOnlyList<string> Positionals { get; private init; } = Array.Empty<string>();
    public IEnumerable<string> OptionNames => _values.Keys.Concat(_switches).Distinct(StringComparer.OrdinalIgnoreCase);

    public static CommandLine Parse(string[] args)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> switches = new(StringComparer.OrdinalIgnoreCase);
        List<string> positionals = [];

        for (int i = 0; i < args.Length; i++)
        {
            string item = args[i];
            if (!item.StartsWith("--", StringComparison.Ordinal))
            {
                positionals.Add(item);
                continue;
            }

            string token = item[2..];
            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentException("An option name cannot be empty.");

            int equals = token.IndexOf('=');
            if (equals >= 0)
            {
                string key = token[..equals];
                string value = token[(equals + 1)..];
                if (string.IsNullOrWhiteSpace(key))
                    throw new ArgumentException("An option name cannot be empty.");
                values[key] = value;
                switches.Remove(key);
                continue;
            }

            if (!FlagOptions.Contains(token) && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                values[token] = args[++i];
                switches.Remove(token);
            }
            else
            {
                switches.Add(token);
                values.Remove(token);
            }
        }

        return new CommandLine(values, switches) { Positionals = positionals };
    }

    public string? Get(string key) => _values.TryGetValue(key, out string? value) ? value : null;

    public bool Has(string key) => _switches.Contains(key) || _values.ContainsKey(key);

    public bool IsSwitch(string key) => _switches.Contains(key);

    public void ValidateKnownOptions(IEnumerable<string> knownOptions)
    {
        HashSet<string> known = new(knownOptions, StringComparer.OrdinalIgnoreCase);
        string[] unknown = OptionNames.Where(name => !known.Contains(name)).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        if (unknown.Length > 0)
            throw new ArgumentException($"Unknown option(s): {string.Join(", ", unknown.Select(name => $"--{name}"))}. Run with --help to list supported rendering options.");
    }

    public int GetInt(string key, int fallback, int? minimum = null, int? maximum = null)
    {
        string? raw = Get(key);
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            throw new ArgumentException($"--{key} must be an integer.");
        if (minimum.HasValue && value < minimum.Value)
            throw new ArgumentOutOfRangeException(key, $"--{key} must be at least {minimum.Value}.");
        if (maximum.HasValue && value > maximum.Value)
            throw new ArgumentOutOfRangeException(key, $"--{key} must be no more than {maximum.Value}.");
        return value;
    }

    public double GetDouble(string key, double fallback, double? minimum = null, double? maximum = null)
    {
        string? raw = Get(key);
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
            throw new ArgumentException($"--{key} must be a finite invariant-culture number.");
        if (minimum.HasValue && value < minimum.Value)
            throw new ArgumentOutOfRangeException(key, $"--{key} must be at least {minimum.Value.ToString(CultureInfo.InvariantCulture)}.");
        if (maximum.HasValue && value > maximum.Value)
            throw new ArgumentOutOfRangeException(key, $"--{key} must be no more than {maximum.Value.ToString(CultureInfo.InvariantCulture)}.");
        return value;
    }

    public bool GetBool(string key, bool fallback)
    {
        if (IsSwitch(key)) return true;
        string? raw = Get(key);
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        if (bool.TryParse(raw, out bool value)) return value;
        if (raw == "1" || raw.Equals("yes", StringComparison.OrdinalIgnoreCase) || raw.Equals("on", StringComparison.OrdinalIgnoreCase)) return true;
        if (raw == "0" || raw.Equals("no", StringComparison.OrdinalIgnoreCase) || raw.Equals("off", StringComparison.OrdinalIgnoreCase)) return false;
        throw new ArgumentException($"--{key} must be true/false, yes/no, on/off, or 1/0.");
    }
}
