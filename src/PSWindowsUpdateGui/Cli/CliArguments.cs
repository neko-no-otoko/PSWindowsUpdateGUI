using System;
using System.Collections.Generic;

namespace PSWindowsUpdateGui.Cli;

internal sealed class CliArguments
{
    private readonly Dictionary<string, List<string>> _options = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

    private CliArguments() { }

    public IList<string> Positionals { get; } = new List<string>();

    public static CliArguments Parse(string[] args)
    {
        var parsed = new CliArguments();
        for (var index = 0; index < args.Length; index++)
        {
            var token = args[index];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                parsed.Positionals.Add(token);
                continue;
            }

            var name = token.Substring(2);
            if (name.Length == 0) throw new FormatException("An option name cannot be empty.");
            if (!parsed._options.TryGetValue(name, out var values))
            {
                values = new List<string>();
                parsed._options.Add(name, values);
            }

            if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
                values.Add(args[++index]);
            else
                values.Add("true");
        }
        return parsed;
    }

    public bool Has(string name) => _options.ContainsKey(name);

    public string Get(string name, string defaultValue = "") =>
        _options.TryGetValue(name, out var values) && values.Count > 0 ? values[values.Count - 1] : defaultValue;

    public IList<string> GetAll(string name) => _options.TryGetValue(name, out var values) ? values : Array.Empty<string>();

    public int GetInt(string name, int defaultValue)
    {
        var value = Get(name);
        if (value.Length == 0) return defaultValue;
        if (!int.TryParse(value, out var result)) throw new FormatException($"--{name} requires an integer.");
        return result;
    }
}
