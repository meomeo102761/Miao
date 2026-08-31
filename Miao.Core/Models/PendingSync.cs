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