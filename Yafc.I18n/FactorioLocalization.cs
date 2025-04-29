using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("Yafc.Model.Tests")]

namespace Yafc.I18n;

public static class FactorioLocalization {
    private static readonly Dictionary<string, string> keys = [];

    public static void Parse(Stream stream) {
        foreach (var (category, key, value) in Read(stream)) {
            keys[$"{category}.{key}"] = CleanupTags(value);
        }
    }

    public static IEnumerable<(string, string, string)> Read(Stream stream) {
        using StreamReader reader = new StreamReader(stream);
        string category = "";

        while (true) {
            string? line = reader.ReadLine();

            if (line == null) {
                break;
            }

            // Trim spaces before keys and all spaces around [categories], but not trailing spaces in values.
            line = line.TrimStart();
            string trimmed = line.TrimEnd();

            if (trimmed.StartsWith('[') && trimmed.EndsWith(']')) {
                category = trimmed[1..^1];
            }
            else {
                int idx = line.IndexOf('=');

                if (idx < 0) {
                    continue;
                }

                string key = line[..idx];
                string val = line[(idx + 1)..];
                yield return (category, key, val);
            }
        }
    }

    private static string CleanupTags(string source) {
        while (true) {
            int tagStart = source.IndexOf('[');

            if (tagStart < 0) {
                return source;
            }

            int tagEnd = source.IndexOf(']', tagStart);

            if (tagEnd < 0) {
                return source;
            }

            source = source.Remove(tagStart, tagEnd - tagStart + 1);
        }
    }

    public static string? Localize(string key) {
        if (keys.TryGetValue(key, out string? val)) {
            return val;
        }

        int lastDash = key.LastIndexOf('-');

        if (lastDash > 0 && int.TryParse(key[(lastDash + 1)..], out int level) && keys.TryGetValue(key[..lastDash], out val)) {
            return val + " " + level;
        }

        return null;
    }

    internal static void Initialize(Dictionary<string, string> newKeys) {
        keys.Clear();
        foreach (var (key, value) in newKeys) {
            keys[key] = value;
        }
    }
}
