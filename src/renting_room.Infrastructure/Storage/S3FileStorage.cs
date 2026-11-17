using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using renting_room.Application.Attachments;
using renting_room.Domain.Attachments;

namespace renting_room.Infrastructure.Storage;

/// <summary>MinIO / AWS S3 / object storage tương thích S3. Bucket private; lỗi kết nối → <see cref="StorageUnavailableException"/> (503, FS-BR-14).</summary>
internal sealed class S3FileStorage : IFileStorage
{
    private readonly IAmazonS3 _client;
    private readonly string _bucket;
    private readonly Protocol _protocol;
    private volatile bool _bucketReady;

    public S3FileStorage(S3StorageOptions options)
    {
        _bucket = options.Bucket;
        // Liên kết ký mặc định là https — MinIO dev chạy http thì liên kết không mở được.
        _protocol = new Uri(options.ServiceUrl).Scheme == Uri.UriSchemeHttp ? Protocol.HTTP : Protocol.HTTPS;
        _client = new AmazonS3Client(
            new BasicAWSCredentials(options.AccessKey, options.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = options.ServiceUrl,
                ForcePathStyle = options.ForcePathStyle,
                AuthenticationRegion = options.Region,
                Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
                MaxErrorRetry = 1
            });
    }

    public StorageProviderKind Kind => StorageProviderKind.S3;

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        await CallAsync(async () =>
        {
            await EnsureBucketAsync(cancellationToken);
            await _client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _bucket, Key = key, InputStream = content, ContentType = contentType, AutoCloseStream = false
            }, cancellationToken);
        });
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken) =>
        await CallAsync(async () =>
        {
            try
            {
                return (await _client.GetObjectAsync(_bucket, key, cancellationToken)).ResponseStream;
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                throw new FileNotFoundException("File is no longer in storage.", ex);
            }
        });

    public async Task DeleteAsync(string key, CancellationToken cancellationToken) =>
        await CallAsync(async () => await _client.DeleteObjectAsync(_bucket, key, cancellationToken));

    public string? GetSignedUrl(string key, TimeSpan lifetime)
    {
        try
        {
            return _client.GetPreSignedURL(new GetPreSignedUrlRequest
            {
                BucketName = _bucket, Key = key, Verb = HttpVerb.GET, Expires = DateTime.UtcNow.Add(lifetime), Protocol = _protocol
            });
        }
        catch (Exception ex) when (ex is AmazonClientException or InvalidOperationException)
        {
            throw new StorageUnavailableException("Cannot create a signed link for the S3 storage.", ex);
        }
    }

    public async Task CheckAvailableAsync(CancellationToken cancellationToken) =>
        await CallAsync(async () => await EnsureBucketAsync(cancellationToken));

    /// <summary>Tạo bucket private ở lần dùng đầu nếu chưa có — người cài không phải tạo tay trong console MinIO.</summary>
    private async Task EnsureBucketAsync(CancellationToken cancellationToken)
    {
        if (_bucketReady)
            return;
        var exists = await Amazon.S3.Util.AmazonS3Util.DoesS3BucketExistV2Async(_client, _bucket);
        if (!exists)
            await _client.PutBucketAsync(new PutBucketRequest { BucketName = _bucket }, cancellationToken);
        _bucketReady = true;
    }

    private static async Task CallAsync(Func<Task> action) =>
        await CallAsync<object?>(async () =>
        {
            await action();
            return null;
        });

    private static async Task<T> CallAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (FileNotFoundException)
        {
            throw;
        }
        catch (Exception ex) when (ex is AmazonClientException or HttpRequestException or IOException or TaskCanceledException or TimeoutException)
        {
            throw new StorageUnavailableException("The S3 / MinIO storage is not reachable or rejected the request.", ex);
        }
    }
}
