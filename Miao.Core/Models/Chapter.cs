using System;
using System.Collections.Generic;

namespace Miao.Core.Models
{
    public enum ChapterStatus
    {
        Unread,
        Reading,
        Edited,
        Favorite
    }

    public class Chapter
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid NovelId { get; set; }
        public Novel? Novel { get; set; }
        public Guid? VolumeId { get; set; }

        public int Number { get; set; }
        public string Title { get; set; } = string.Empty;
        public string TranslatedTitle { get; set; } = string.Empty;

        public string DisplayTitle =>
            string.IsNullOrWhiteSpace(TranslatedTitle) ? Title : TranslatedTitle;

        public string OriginalContent { get; set; } = string.Empty;
        public string DisplayContent { get; set; } = string.Empty;

        public ChapterStatus Status { get; set; } = ChapterStatus.Unread;

        public string SourceUrl { get; set; } = string.Empty;
        public DateTime DownloadedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastEditedAt { get; set; }

        public List<NoteEntry> Notes { get; set; } = new();
        public bool IsPinned { get; set; } = false;
    }
}