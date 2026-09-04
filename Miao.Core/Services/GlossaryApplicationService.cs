using System;
using System.Linq;
using System.Threading.Tasks;
using Miao.Core.Data;
using Miao.Core.Models;

namespace Miao.Core.Services
{
    public static class GlossaryApplicationService
    {
        public static GlossarySetEntry? FindEntryByOriginalTerm(MiaoDbContext db, Guid glossarySetId, string originalTerm)
        {
            if (string.IsNullOrWhiteSpace(originalTerm)) return null;

            var normalized = originalTerm.Trim().ToLower();
            return db.GlossarySetEntries
                .FirstOrDefault(x => x.GlossarySetId == glossarySetId && x.OriginalTerm.ToLower() == normalized);
        }

        public static async Task DeleteEntryAndRevertAsync(MiaoDbContext db, Guid entryId)
        {
            var entry = db.GlossarySetEntries.Find(entryId);
            if (entry == null) return;

            var oldTranslated = entry.TranslatedTerm;
            var originalTerm = entry.OriginalTerm;
            var setId = entry.GlossarySetId;

            db.GlossarySetEntries.Remove(entry);
            await db.SaveChangesAsync();

            if (string.IsNullOrWhiteSpace(oldTranslated)) return;

            string newTranslated = originalTerm;
            try
            {
                var translator = TranslationService.CreateFromSettings();
                var t = (await translator.TranslateChapterAsync(originalTerm)).Trim();
                if (!string.IsNullOrWhiteSpace(t)) newTranslated = t;
            }
            catch { /* dịch lỗi -> vẫn đã xóa entry, chỉ là không khôi phục được bản dịch máy mới */ }

            if (oldTranslated == newTranslated) return;

            var novelIds = db.NovelGlossarySets.Where(ns => ns.GlossarySetId == setId).Select(ns => ns.NovelId).ToList();

            foreach (var novelId in novelIds)
            {
                var novel = db.Novels.Find(novelId);
                if (novel == null) continue;

                if (!string.IsNullOrWhiteSpace(novel.CustomTitle) && novel.CustomTitle.Contains(oldTranslated, StringComparison.Ordinal))
                    novel.CustomTitle = novel.CustomTitle.Replace(oldTranslated, newTranslated);
                else if (!string.IsNullOrWhiteSpace(novel.TranslatedTitle) && novel.TranslatedTitle.Contains(oldTranslated, StringComparison.Ordinal))
                    novel.TranslatedTitle = novel.TranslatedTitle.Replace(oldTranslated, newTranslated);

                foreach (var chapter in db.Chapters.Where(c => c.NovelId == novelId).ToList())
                {
                    if (!string.IsNullOrWhiteSpace(chapter.TranslatedTitle) && chapter.TranslatedTitle.Contains(oldTranslated, StringComparison.Ordinal))
                        chapter.TranslatedTitle = chapter.TranslatedTitle.Replace(oldTranslated, newTranslated);

                    var content = chapter.DisplayContent ?? "";
                    if (content.Contains(oldTranslated, StringComparison.Ordinal))
                        chapter.DisplayContent = content.Replace(oldTranslated, newTranslated);
                }
            }

            await db.SaveChangesAsync();
        }

        public static string Apply(MiaoDbContext db, Guid novelId, string? translatedText)
        {
            if (string.IsNullOrWhiteSpace(translatedText))
                return translatedText ?? "";

            var setIds = db.NovelGlossarySets
                .Where(ns => ns.NovelId == novelId)
                .Select(ns => ns.GlossarySetId)
                .ToList();

            if (setIds.Count == 0)
                return translatedText;

            var entries = db.GlossarySetEntries
                .Where(e => setIds.Contains(e.GlossarySetId))
                .Where(e => !string.IsNullOrWhiteSpace(e.OriginalTerm) && !string.IsNullOrWhiteSpace(e.TranslatedTerm))
                .Where(e => e.OriginalTerm != e.TranslatedTerm)
                .OrderByDescending(e => e.OriginalTerm.Length)
                .ToList();

            foreach (var entry in entries)
                translatedText = translatedText.Replace(entry.OriginalTerm, entry.TranslatedTerm, StringComparison.Ordinal);

            return translatedText;
        }
    }
}