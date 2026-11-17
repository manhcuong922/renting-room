using Microsoft.Extensions.Options;
using renting_room.Application.Attachments;
using renting_room.Domain.Attachments;

namespace renting_room.Infrastructure.Storage;

/// <summary>Local luôn có; S3 có khi đã cấu hình đủ (kể cả khi Provider=Local — vẫn đọc được tệp cũ, FS-BR-15). Không kết nối gì lúc khởi động (FS-BR-12).</summary>
internal sealed class FileStorageSelector : IFileStorageSelector
{
    private readonly StorageProviderKind _provider;
    private readonly LocalFileStorage _local;
    private readonly S3FileStorage? _s3;

    public FileStorageSelector(IOptions<StorageOptions> options)
    {
        var value = options.Value;
        _provider = value.Provider;
        _local = new LocalFileStorage(value.Local.RootPath);
        _s3 = value.S3.IsConfigured ? new S3FileStorage(value.S3) : null;
    }

    public IFileStorage ForNewFiles => For(_provider);

    public IFileStorage For(StorageProviderKind kind) => kind switch
    {
        StorageProviderKind.Local => _local,
        StorageProviderKind.S3 => _s3 ?? throw new StorageUnavailableException("S3 / MinIO storage is not configured."),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
