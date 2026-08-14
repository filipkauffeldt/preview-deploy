using System.Formats.Tar;
using PreviewDeploy.Server.Deployments;

namespace PreviewDeploy.Server.Tests;

public sealed class BuildContextBuilderTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("build-context-").FullName;

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void CreateTar_ExcludesGitDirectory()
    {
        Directory.CreateDirectory(Path.Combine(_directory, ".git"));
        File.WriteAllText(Path.Combine(_directory, "app.cs"), "x");
        File.WriteAllText(Path.Combine(_directory, ".git", "config"), "token");

        var entries = TarEntries();

        Assert.Equal(["app.cs"], entries);
    }

    [Fact]
    public void CreateTar_IncludesAllFiles_WithoutDockerignore()
    {
        File.WriteAllText(Path.Combine(_directory, "a.txt"), "x");
        Directory.CreateDirectory(Path.Combine(_directory, "sub"));
        File.WriteAllText(Path.Combine(_directory, "sub", "b.txt"), "x");

        var entries = TarEntries();

        Assert.Equal(["a.txt", "sub/b.txt"], entries);
    }

    [Fact]
    public void CreateTar_HonorsDockerignore()
    {
        File.WriteAllText(Path.Combine(_directory, ".dockerignore"), "secret.txt\nignored/\n");
        File.WriteAllText(Path.Combine(_directory, "secret.txt"), "x");
        File.WriteAllText(Path.Combine(_directory, "kept.txt"), "x");
        Directory.CreateDirectory(Path.Combine(_directory, "ignored"));
        File.WriteAllText(Path.Combine(_directory, "ignored", "inner.txt"), "x");

        var entries = TarEntries();

        Assert.Equal([".dockerignore", "kept.txt"], entries);
    }

    [Fact]
    public void CreateTar_HonorsDockerignoreNegation()
    {
        File.WriteAllText(Path.Combine(_directory, ".dockerignore"), "*.txt\n!keep.txt\n");
        File.WriteAllText(Path.Combine(_directory, "keep.txt"), "x");
        File.WriteAllText(Path.Combine(_directory, "drop.txt"), "x");

        var entries = TarEntries();

        Assert.Equal([".dockerignore", "keep.txt"], entries);
    }

    [Fact]
    public void CreateTar_HonorsDockerignoreNestedPattern()
    {
        File.WriteAllText(Path.Combine(_directory, ".dockerignore"), "**/bin\n");
        Directory.CreateDirectory(Path.Combine(_directory, "src", "bin"));
        File.WriteAllText(Path.Combine(_directory, "src", "bin", "out.dll"), "x");
        File.WriteAllText(Path.Combine(_directory, "src", "Program.cs"), "x");

        var entries = TarEntries();

        Assert.Equal([".dockerignore", "src/Program.cs"], entries);
    }

    private List<string> TarEntries()
    {
        using var stream = BuildContextBuilder.CreateTar(_directory);
        using var reader = new TarReader(stream);
        var entries = new List<string>();
        TarEntry? entry;
        while ((entry = reader.GetNextEntry()) is not null)
        {
            entries.Add(entry.Name);
        }

        entries.Sort();
        return entries;
    }
}
