using Miao.Core.Data;
using Miao.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Miao.Core.Services
{
    public static class CharacterGroupService
    {
        public static Task<List<CharacterGroup>> GetAllAsync(MiaoDbContext db) =>
            db.CharacterGroups.OrderBy(g => g.SortOrder).ToListAsync();

        public static async Task<CharacterGroup> CreateAsync(MiaoDbContext db, string name, byte[]? coverPng)
        {
            var group = new CharacterGroup { Name = name, SortOrder = await NextSortOrderAsync(db) };
            db.CharacterGroups.Add(group);
            await db.SaveChangesAsync();

            if (coverPng != null)
            {
                group.CoverImagePath = CharacterImageStorage.SaveGroupCoverBytes(group.Id, coverPng);
                await db.SaveChangesAsync();
            }
            return group;
        }

        public static async Task UpdateAsync(MiaoDbContext db, Guid groupId, string name, byte[]? coverPng)
        {
            var group = await db.CharacterGroups.FindAsync(groupId)
                ?? throw new InvalidOperationException("Không tìm thấy dàn nhân vật.");
            group.Name = name;
            if (coverPng != null)
                group.CoverImagePath = CharacterImageStorage.SaveGroupCoverBytes(groupId, coverPng);
            await db.SaveChangesAsync();
        }

        public static async Task DeleteAsync(MiaoDbContext db, Guid groupId)
        {
            var group = await db.CharacterGroups.FindAsync(groupId);
            if (group == null) return;
            db.CharacterGroups.Remove(group);
            await db.SaveChangesAsync();
            CharacterImageStorage.DeleteGroupFolder(groupId);
            CharacterScanCache.InvalidateAll();
        }

        private static async Task<int> NextSortOrderAsync(MiaoDbContext db) =>
            (await db.CharacterGroups.MaxAsync(g => (int?)g.SortOrder) ?? -1) + 1;

        public static async Task UpdateBannerFocalAsync(MiaoDbContext db, Guid groupId, double focalX, double focalY)
        {
            var group = await db.CharacterGroups.FindAsync(groupId);
            if (group == null) return;
            group.BannerFocalX = Math.Clamp(focalX, 0, 1);
            group.BannerFocalY = Math.Clamp(focalY, 0, 1);
            await db.SaveChangesAsync();
        }

        public static async Task UpdateBannerScaleAsync(MiaoDbContext db, Guid groupId, double scale)
        {
            var group = await db.CharacterGroups.FindAsync(groupId);
            if (group == null) return;
            group.BannerScale = scale;
            await db.SaveChangesAsync();
        }
    }
}