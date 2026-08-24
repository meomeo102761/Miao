using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.IO;
using Miao.Core.Services;

namespace Miao.Core.Models
{
    public class Novel
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public NovelType Type { get; set; } = NovelType.Downloaded;

        public string Title { get; set; } = string.Empty;
        public string TranslatedTitle { get; set; } = string.Empty;
        public string CustomTitle { get; set; } = string.Empty;
        public string DisplayTitle =>
            !string.IsNullOrWhiteSpace(CustomTitle) ? CustomTitle :
            !string.IsNullOrWhiteSpace(TranslatedTitle) ? TranslatedTitle : Title;

        public string Author { get; set; } = string.Empty;
        public string TranslatedAuthor { get; set; } = string.Empty;
        public string DisplayAuthor =>
            !string.IsNullOrWhiteSpace(TranslatedAuthor) ? TranslatedAuthor : Author;
        public string SourceUrl { get; set; } = string.Empty;
        public string SourceDescription { get; set; } = string.Empty;
        public string CoverImagePath { get; set; } = string.Empty;

        [NotMapped]
        public string CoverImageSource
        {
            get
            {
                var defaultCover = Path.Combine(
                    AppSettingsService.Instance.Settings.DataFolder,
                    "Assets",
                    "default-cover.jpg");

                if (string.IsNullOrWhiteSpace(CoverImagePath))
                    return defaultCover;

                var path = CoverImagePath;

                if (!Path.IsPathRooted(path))
                {
                    path = Path.Combine(
                        AppSettingsService.Instance.Settings.DataFolder,
                        path);
                }

                return File.Exists(path) ? path : defaultCover;
            }
        }

        public string Tags { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string TranslatedDescription { get; set; } = string.Empty;
        public string DisplayDescription =>
            !string.IsNullOrWhiteSpace(TranslatedDescription) ? TranslatedDescription : Description;
        public string Status { get; set; } = "Chưa xác minh";

        public bool IsFavorite { get; set; } = false;
        public bool IsDownloaded { get; set; } = false;

        public int LastReadChapterNumber { get; set; } = 0;

        [NotMapped]
        public int TotalChapterCount { get; set; } = 0;

        [NotMapped]
        public string DirectionTag { get; set; } = string.Empty;

        [NotMapped]
        public string ReadProgress =>
            LastReadChapterNumber > TotalChapterCount && TotalChapterCount > 0
                ? $"Đã đọc đến chương {LastReadChapterNumber}"
                : $"Đã đọc {LastReadChapterNumber}/{TotalChapterCount}";

        public string GetFirstTag()
        {
            if (string.IsNullOrWhiteSpace(Tags))
                return string.Empty;

            return Tags
                .Split(new[] { ',', '|', ';', '，', '、' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                ?? string.Empty;
        }

        public DateTime AddedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastUpdatedAt { get; set; }

        public List<Chapter> Chapters { get; set; } = new();
    }
}