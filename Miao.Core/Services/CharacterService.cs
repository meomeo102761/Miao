using Miao.Core.Data;
using Miao.Core.Models;
using Miao.Core.TextScanning;
using Microsoft.EntityFrameworkCore;

namespace Miao.Core.Services
{
    public static class CharacterService
    {
        public static async Task<Character> CreateCharacterAsync(
            MiaoDbContext db, Guid groupId, string name, string imagePath, string description, int sortOrder)
        {
            var character = new Character
            {
                CharacterGroupId = groupId,
                Name = name,
                ImagePath = imagePath,
                Description = description,
                SortOrder = sortOrder,
            };

            character.Aliases.Add(new CharacterAlias
            {
                CharacterId = character.Id,
                AliasText = name,
                NormalizedAliasText = VietnameseTextNormalizer.Normalize(name),
            });

            db.Characters.Add(character);
            await db.SaveChangesAsync();
            return character;
        }

        public static async Task<bool> TryAddOrUpdateAliasAsync(
            MiaoDbContext db, Guid characterId, Guid aliasIdOrEmpty, string aliasText, Guid contextNovelId)
        {
            var normalized = VietnameseTextNormalizer.Normalize(aliasText);
            var scopeCharacterIds = await GetEffectiveScanScopeCharacterIdsAsync(db, contextNovelId);

            bool duplicate = await db.CharacterAliases
                .Where(a => a.Id != aliasIdOrEmpty && a.IsEnabledForScan)
                .Where(a => scopeCharacterIds.Contains(a.CharacterId))
                .AnyAsync(a => a.NormalizedAliasText == normalized);

            if (duplicate) return false;

            var existing = aliasIdOrEmpty != Guid.Empty
                ? await db.CharacterAliases.FindAsync(aliasIdOrEmpty)
                : null;

            if (existing != null)
            {
                existing.AliasText = aliasText;
                existing.NormalizedAliasText = normalized;
            }
            else
            {
                db.CharacterAliases.Add(new CharacterAlias
                {
                    CharacterId = characterId,
                    AliasText = aliasText,
                    NormalizedAliasText = normalized,
                });
            }

            await db.SaveChangesAsync();
            return true;
        }

        public static async Task<HashSet<Guid>> GetEffectiveScanScopeCharacterIdsAsync(MiaoDbContext db, Guid novelId)
        {
            if (novelId == Guid.Empty)
            {
                var allIds = await db.Characters.Select(c => c.Id).ToListAsync();
                return allIds.ToHashSet();
            }

            var groupIds = await db.NovelCharacterGroups
                .Where(nc => nc.NovelId == novelId)
                .Select(nc => nc.CharacterGroupId)
                .ToListAsync();

            var ids = await db.Characters
                .Where(c => groupIds.Contains(c.CharacterGroupId))
                .Select(c => c.Id)
                .ToListAsync();
            return ids.ToHashSet();
        }

        public static async Task UpdateCharacterAsync(
            MiaoDbContext db, Guid characterId, string name, byte[]? newImagePng, string description)
        {
            var character = await db.Characters.Include(c => c.Aliases).FirstOrDefaultAsync(c => c.Id == characterId)
                ?? throw new InvalidOperationException("Không tìm thấy nhân vật.");

            character.Name = name;
            character.Description = description;

            if (newImagePng != null)
                character.ImagePath = CharacterImageStorage.SaveCharacterImageBytes(character.CharacterGroupId, characterId, newImagePng);

            await db.SaveChangesAsync();
        }

        public static async Task DeleteCharacterAsync(MiaoDbContext db, Guid characterId)
        {
            var character = await db.Characters.FindAsync(characterId);
            if (character == null) return;
            var groupId = character.CharacterGroupId;
            db.Characters.Remove(character);
            await db.SaveChangesAsync();
            CharacterImageStorage.DeleteCharacterImage(groupId, characterId);
            CharacterScanCache.InvalidateAll();
        }

        public static async Task DeleteAliasAsync(MiaoDbContext db, Guid aliasId)
        {
            var alias = await db.CharacterAliases.FindAsync(aliasId);
            if (alias == null) return;
            db.CharacterAliases.Remove(alias);
            await db.SaveChangesAsync();
            CharacterScanCache.InvalidateAll();
        }

        public static async Task<bool> AddAliasAsync(MiaoDbContext db, Guid characterId, string aliasText)
        {
            aliasText = aliasText.Trim();
            if (string.IsNullOrEmpty(aliasText)) return false;

            var normalized = VietnameseTextNormalizer.Normalize(aliasText);

            bool duplicateElsewhere = await db.CharacterAliases
                .Where(a => a.CharacterId != characterId && a.IsEnabledForScan)
                .AnyAsync(a => a.NormalizedAliasText == normalized);
            if (duplicateElsewhere) return false;

            bool alreadyHas = await db.CharacterAliases
                .AnyAsync(a => a.CharacterId == characterId && a.NormalizedAliasText == normalized);
            if (alreadyHas) return true;

            db.CharacterAliases.Add(new CharacterAlias { CharacterId = characterId, AliasText = aliasText, NormalizedAliasText = normalized });
            await db.SaveChangesAsync();
            CharacterScanCache.InvalidateAll();
            return true;
        }
    }
}