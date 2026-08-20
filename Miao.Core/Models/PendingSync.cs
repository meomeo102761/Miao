using System;

namespace Miao.Core.Models
{
    public enum SyncAction
    {
        Create,
        Update,
        Delete
    }

    public enum SyncEntityType
    {
        Novel,
        Chapter,
        GlossarySet,
        GlossarySetEntry,
        NovelGlossarySet,
        CharacterGroup,
        Character,
        CharacterAlias,
        NovelCharacterGroup,
        CustomLibrary,
        CustomLibraryNovel,
        Tag,
        NovelTag,
        NovelLink,
        NovelSource,
        Volume,
        NoteEntry
    }

    // Mỗi thay đổi local được ghi 1 dòng ở đây trước khi đẩy lên Google Drive.
    // SyncService đọc bảng này để biết cần push gì, và xoá sau khi push thành công.
    public class PendingSync
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public SyncEntityType EntityType { get; set; }
        public Guid EntityId { get; set; }
        public SyncAction Action { get; set; }
        public string DeviceId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}