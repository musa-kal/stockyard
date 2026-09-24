namespace Stockyard.Exporter;

public sealed class FileSystemDestination(string root) : IExportDestination
{
    public async Task<string> WriteAsync(string relativePath, Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Write next to the target and move, so a reader never sees half a file.
        var temporary = path + ".tmp";
        await using (var file = File.Create(temporary))
        {
            await content.CopyToAsync(file, cancellationToken);
        }

        File.Move(temporary, path, overwrite: true);
        return path;
    }
}
