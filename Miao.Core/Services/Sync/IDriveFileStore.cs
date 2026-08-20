namespace Miao.Core.Services.Sync
{
    // Core không tự lo OAuth/khởi tạo DriveService — việc đó khác nhau giữa
    // Desktop (mở trình duyệt loopback) và Android (Custom Tabs), nên chỉ cần
    // 1 interface tối giản, Desktop/Android tự implement rồi truyền vào.
    public interface IDriveFileStore
    {
        Task<bool> UploadAsync(string relativePath, string content, CancellationToken ct);
        Task<string?> DownloadAsync(string relativePath, CancellationToken ct);
        Task<IReadOnlyList<DriveFileInfo>> ListAsync(string folderPrefix, CancellationToken ct);
        Task<bool> DeleteAsync(string relativePath, CancellationToken ct);
    }

    public record DriveFileInfo(string RelativePath, DateTime ModifiedAtUtc);
}