using System;

namespace Miao.Core.Services.Sync
{
    // "Phong bì" chung để đóng gói bất kỳ entity nào khi gửi lên/nhận về từ Drive.
    // Nhờ có Payload dạng JSON string nên SyncService không cần biết trước
    // cấu trúc cụ thể của từng loại entity (Novel, Chapter, Character...).
    public class SyncEnvelope
    {
        public string EntityType { get; set; } = string.Empty; // "Novel", "Chapter", "Character"...
        public Guid EntityId { get; set; }
        public string DeviceId { get; set; } = string.Empty;
        public DateTime UpdatedAtUtc { get; set; }
        public bool IsDeleted { get; set; }
        public string PayloadJson { get; set; } = string.Empty; // JSON của chính entity đó
    }
}