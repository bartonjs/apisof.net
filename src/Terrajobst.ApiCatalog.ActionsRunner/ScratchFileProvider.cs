namespace GenUsagePlanner;

public sealed class ScratchFileProvider
{
    public static ScratchFileProvider Instance { get; } = new();

    public string GetScratchFilePath(string fileName)
    {
        var path = Path.Join(Environment.CurrentDirectory, "scratch", fileName);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        return path;
    }
}