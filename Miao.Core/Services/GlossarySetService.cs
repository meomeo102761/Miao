using System.Linq;
using Miao.Core.Data;
using Miao.Core.Models;

namespace Miao.Core.Services
{
    public static class GlossarySetService
    {
        public static void CreateDefaultForNovel(MiaoDbContext db, Novel novel)
        {
            var set = new GlossarySet
            {
                Name = novel.DisplayTitle,
                IsShared = false,
                OwnerNovelId = novel.Id
            };
            db.GlossarySets.Add(set);
            db.SaveChanges();

            db.NovelGlossarySets.Add(new NovelGlossarySet
            {
                NovelId = novel.Id,
                GlossarySetId = set.Id
            });
            db.SaveChanges();
        }

        public static void BackfillMissingDefaults(MiaoDbContext db)
        {
            var novelIdsWithDefault = db.GlossarySets
                .Where(s => s.OwnerNovelId != null)
                .Select(s => s.OwnerNovelId!.Value)
                .ToHashSet();

            var missingNovels = db.Novels
                .Where(n => !novelIdsWithDefault.Contains(n.Id))
                .ToList();

            foreach (var novel in missingNovels)
                CreateDefaultForNovel(db, novel);
        }
    }
}