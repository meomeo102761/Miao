namespace Miao.Core.Services.Sync
{
    public interface IDriveFileStore
    {
        Task<bool> UploadAsync(string relativePath, string content, CancellationToken ct);
        Task<string?> DownloadAsync(string relativePath, CancellationToken ct);
        Task<IReadOnlyList<DriveFileInfo>> ListAsync(string folderPrefix, CancellationToken ct);
        Task<bool> DeleteAsync(string relativePath, CancellationToken ct);
    }

    public record DriveFileInfo(string RelativePath, DateTime ModifiedAtUtc);
}