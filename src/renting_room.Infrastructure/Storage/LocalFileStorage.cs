using renting_room.Application.Attachments;
using renting_room.Domain.Attachments;

namespace renting_room.Infrastructure.Storage;

/// <summary>Lưu file trên ổ đĩa máy chủ (mặc định, dev / một máy chủ). Thư mục nằm ngoài wwwroot — chỉ truy cập qua API.</summary>
internal sealed class LocalFileStorage(string rootPath) : IFileStorage
{
    private readonly string _root = Path.GetFullPath(rootPath);

    public StorageProviderKind Kind => StorageProviderKind.Local;

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        var path = PathFor(key);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                await content.CopyToAsync(file, cancellationToken);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);
            throw new StorageUnavailableException("Cannot write to the local storage folder.", ex);
        }
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            return Task.FromResult<Stream>(new FileStream(PathFor(key), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true));
        }
        catch (DirectoryNotFoundException ex)
        {
            throw new FileNotFoundException("File is no longer in storage.", ex);
        }
        catch (FileNotFoundException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new StorageUnavailableException("Cannot read from the local storage folder.", ex);
        }
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            File.Delete(PathFor(key));
            return Task.CompletedTask;
        }
        catch (DirectoryNotFoundException)
        {
            return Task.CompletedTask;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new StorageUnavailableException("Cannot delete from the local storage folder.", ex);
        }
    }

    public string? GetSignedUrl(string key, TimeSpan lifetime) => null;

    public Task CheckAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(_root);
            var probe = Path.Combine(_root, ".probe-" + Guid.NewGuid().ToString("N"));
            File.WriteAllBytes(probe, [0]);
            File.Delete(probe);
            return Task.CompletedTask;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new StorageUnavailableException("The local storage folder is not writable.", ex);
        }
    }

    /// <summary>Khóa do hệ thống sinh (FS-BR-05) — vẫn chặn thoát khỏi thư mục gốc phòng khi có lỗi lập trình.</summary>
    private string PathFor(string key)
    {
        var full = Path.GetFullPath(Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Storage key escapes the storage folder.", nameof(key));
        return full;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}
