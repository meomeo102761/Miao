using Microsoft.EntityFrameworkCore;
using Miao.Core.Data;
using Miao.Core.Models;

namespace Miao.Core.Services
{
    public static class NovelCharacterGroupService
    {
        public static Task<List<Guid>> GetAttachedGroupIdsAsync(MiaoDbContext db, Guid novelId) =>
            db.NovelCharacterGroups
                .Where(nc => nc.NovelId == novelId)
                .Select(nc => nc.CharacterGroupId)
                .ToListAsync();

        public static Task<List<CharacterGroup>> GetAvailableGroupsForNovelAsync(MiaoDbContext db, Guid novelId) =>
            db.CharacterGroups
                .Where(g => g.IsShared || g.OwnerNovelId == novelId || g.OwnerNovelId == null)
                .OrderBy(g => g.SortOrder)
                .ToListAsync();

        public static async Task AttachAsync(MiaoDbContext db, Guid novelId, Guid groupId)
        {
            bool exists = await db.NovelCharacterGroups
                .AnyAsync(nc => nc.NovelId == novelId && nc.CharacterGroupId == groupId);
            if (exists) return; 

            db.NovelCharacterGroups.Add(new NovelCharacterGroup { NovelId = novelId, CharacterGroupId = groupId });
            await db.SaveChangesAsync();
            CharacterScanCache.InvalidateNovel(novelId);
        }

        public static async Task DetachAsync(MiaoDbContext db, Guid novelId, Guid groupId)
        {
            var row = await db.NovelCharacterGroups
                .FirstOrDefaultAsync(nc => nc.NovelId == novelId && nc.CharacterGroupId == groupId);
            if (row == null) return;

            db.NovelCharacterGroups.Remove(row);
            await db.SaveChangesAsync();
            CharacterScanCache.InvalidateNovel(novelId);
        }
    }
}