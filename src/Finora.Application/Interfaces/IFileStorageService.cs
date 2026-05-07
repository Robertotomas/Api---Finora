namespace Finora.Application.Interfaces;

public interface IFileStorageService
{
    Task UploadAsync(string path, byte[] data, string contentType, CancellationToken cancellationToken = default);
    Task<byte[]?> DownloadAsync(string path, CancellationToken cancellationToken = default);
    Task DeleteAsync(string path, CancellationToken cancellationToken = default);
}
