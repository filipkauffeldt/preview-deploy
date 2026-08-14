using System.Formats.Tar;
using Microsoft.Extensions.FileSystemGlobbing;

namespace PreviewDeploy.Server.Deployments;

public static class BuildContextBuilder
{
    private sealed record IgnoreRule(string Pattern, bool Negated);

    public static Stream CreateTar(string directory)
    {
        var rules = LoadIgnoreRules(directory);
        var stream = new MemoryStream();
        using (var writer = new TarWriter(stream, TarEntryFormat.Ustar, leaveOpen: true))
        {
            foreach (var file in EnumerateFiles(directory, rules))
            {
                var relativePath = Path.GetRelativePath(directory, file).Replace(Path.DirectorySeparatorChar, '/');
                var entry = new UstarTarEntry(TarEntryType.RegularFile, relativePath)
                {
                    DataStream = File.OpenRead(file),
                };
                writer.WriteEntry(entry);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static IEnumerable<string> EnumerateFiles(string directory, List<IgnoreRule> rules) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(file => !IsExcluded(directory, file, rules));

    private static List<IgnoreRule> LoadIgnoreRules(string directory)
    {
        var path = Path.Combine(directory, ".dockerignore");
        if (!File.Exists(path))
        {
            return [];
        }

        return File.ReadLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.StartsWith('!')
                ? new IgnoreRule(line[1..].Trim(), Negated: true)
                : new IgnoreRule(line, Negated: false))
            .ToList();
    }

    private static bool IsExcluded(string directory, string file, List<IgnoreRule> rules)
    {
        var relative = Path.GetRelativePath(directory, file).Replace(Path.DirectorySeparatorChar, '/');
        if (relative.Split('/').Contains(".git"))
        {
            return true;
        }

        var lastMatch = (bool?)null;
        foreach (var rule in rules)
        {
            if (Matches(relative, rule.Pattern))
            {
                lastMatch = !rule.Negated;
            }
        }

        return lastMatch ?? false;
    }

    private static bool Matches(string relative, string pattern)
    {
        if (pattern.EndsWith('/'))
        {
            pattern += "**";
        }
        else if (!pattern.Contains('/'))
        {
            pattern = "**/" + pattern;
        }

        var matcher = new Matcher();
        matcher.AddInclude(pattern);
        if (matcher.Match(relative).HasMatches)
        {
            return true;
        }

        // A pattern matching a directory (e.g. **/bin) also excludes files beneath it.
        var parts = relative.Split('/');
        for (var i = 1; i < parts.Length; i++)
        {
            if (matcher.Match(string.Join('/', parts[..i])).HasMatches)
            {
                return true;
            }
        }

        return false;
    }
}
