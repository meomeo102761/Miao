using System;
using System.Collections.Generic;
using System.Linq;
using Miao.Core.Data;
using Miao.Core.Models;

namespace Miao.Core.Services
{
    // Gán TotalChapterCount + DirectionTag cho 1 danh sách Novel — logic được rút ra
    // từ LibraryPage.LoadNovels(), dùng chung cho mọi trang hiển thị NovelCardTemplate.
    public static class NovelEnrichmentService
    {
        private sealed class NovelTagInfo
        {
            public string Name { get; init; } = string.Empty;
            public string Category { get; init; } = string.Empty;
        }

        private static readonly HashSet<string> KnownDirectionTags = new(StringComparer.OrdinalIgnoreCase)
        {
            "Ngôn tình", "Đam mỹ", "Bách hợp", "Vô CP", "Không CP",
            "BG", "BL", "GL", "言情", "纯爱", "百合", "无CP"
        };

        public static void ApplyDisplayInfo(MiaoDbContext db, List<Novel> novels)
        {
            if (novels.Count == 0) return;

            var novelIds = novels.Select(n => n.Id).ToHashSet();

            var chapterCounts = db.Chapters
                .Where(c => novelIds.Contains(c.NovelId))
                .GroupBy(c => c.NovelId)
                .Select(g => new { NovelId = g.Key, Count = g.Count() })
                .ToDictionary(x => x.NovelId, x => x.Count);

            var tagsByNovel = (
                from nt in db.NovelTags
                where novelIds.Contains(nt.NovelId)
                join t in db.Tags on nt.TagId equals t.Id
                select new { nt.NovelId, t.Name, t.Category })
                .ToList()
                .GroupBy(x => x.NovelId)
                .ToDictionary(g => g.Key, g => g.Select(x => new NovelTagInfo { Name = x.Name, Category = x.Category }).ToList());

            foreach (var novel in novels)
            {
                novel.TotalChapterCount = chapterCounts.TryGetValue(novel.Id, out var count) ? count : 0;
                novel.DirectionTag = GetDirectionTag(tagsByNovel.TryGetValue(novel.Id, out var tags) ? tags : new List<NovelTagInfo>());
            }
        }

        private static string GetDirectionTag(IEnumerable<NovelTagInfo> tags)
        {
            var direction = tags.FirstOrDefault(t =>
                t.Category.Contains("hướng", StringComparison.OrdinalIgnoreCase)
                || t.Category.Contains("giới tính", StringComparison.OrdinalIgnoreCase)
                || t.Category.Contains("giới", StringComparison.OrdinalIgnoreCase));

            if (direction != null && !string.IsNullOrWhiteSpace(direction.Name))
                return direction.Name.Trim();

            return tags.Select(t => t.Name.Trim()).FirstOrDefault(KnownDirectionTags.Contains) ?? string.Empty;
        }
    }
}