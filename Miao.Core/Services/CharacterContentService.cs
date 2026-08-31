using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Miao.Core.Data;
using Miao.Core.Models;
using Miao.Core.TextScanning;

namespace Miao.Core.Services
{
    public static class CharacterContentService
    {
        public const string NameEntryLabel = "Tên";

        public static Task<List<CharacterInfoSection>> GetInfoSectionsAsync(MiaoDbContext db, Guid characterId) =>
            db.CharacterInfoSections
                .Include(s => s.Entries.OrderBy(e => e.SortOrder))
                .Where(s => s.CharacterId == characterId)
                .OrderBy(s => s.SortOrder)
                .ToListAsync();

        public static async Task<CharacterInfoSection> AddInfoSectionAsync(MiaoDbContext db, Guid characterId, string title)
        {
            var count = await db.CharacterInfoSections.CountAsync(s => s.CharacterId == characterId);
            var section = new CharacterInfoSection { CharacterId = characterId, Title = title, SortOrder = count };
            db.CharacterInfoSections.Add(section);
            await db.SaveChangesAsync();
            return section;
        }

        public static async Task RenameInfoSectionAsync(MiaoDbContext db, Guid sectionId, string title)
        {
            var section = await db.CharacterInfoSections.FindAsync(sectionId);
            if (section == null) return;
            section.Title = title;
            await db.SaveChangesAsync();
        }

        public static async Task DeleteInfoSectionAsync(MiaoDbContext db, Guid sectionId)
        {
            var section = await db.CharacterInfoSections.FindAsync(sectionId);
            if (section == null) return;
            db.CharacterInfoSections.Remove(section);
            await db.SaveChangesAsync();
            CharacterScanCache.InvalidateAll();
        }

        public static async Task<CharacterInfoEntry> AddInfoEntryAsync(MiaoDbContext db, Guid sectionId, string label, string value)
        {
            var count = await db.CharacterInfoEntries.CountAsync(e => e.CharacterInfoSectionId == sectionId);
            var entry = new CharacterInfoEntry { CharacterInfoSectionId = sectionId, Label = label, Value = value, SortOrder = count };
            db.CharacterInfoEntries.Add(entry);
            await db.SaveChangesAsync();

            if (label == NameEntryLabel)
            {
                var section = await db.CharacterInfoSections.FindAsync(sectionId);
                if (section != null) await SyncNameAliasesAsync(db, section.CharacterId, value);
            }
            return entry;
        }

        public static async Task<List<string>> UpdateInfoEntryAsync(MiaoDbContext db, Guid entryId, string label, string value)
        {
            var entry = await db.CharacterInfoEntries.Include(e => e.Section).FirstOrDefaultAsync(e => e.Id == entryId);
            if (entry == null) return new();

            entry.Label = label;
            entry.Value = value;
            await db.SaveChangesAsync();

            if (label == NameEntryLabel && entry.Section != null)
                return await SyncNameAliasesAsync(db, entry.Section.CharacterId, value);

            return new();
        }

        public static async Task DeleteInfoEntryAsync(MiaoDbContext db, Guid entryId)
        {
            var entry = await db.CharacterInfoEntries.Include(e => e.Section).FirstOrDefaultAsync(e => e.Id == entryId);
            if (entry == null) return;
            bool wasNameEntry = entry.Label == NameEntryLabel;
            var characterId = entry.Section?.CharacterId ?? Guid.Empty;

            db.CharacterInfoEntries.Remove(entry);
            await db.SaveChangesAsync();

            if (wasNameEntry && characterId != Guid.Empty)
                await SyncNameAliasesAsync(db, characterId, ""); 
        }

        private static async Task<List<string>> SyncNameAliasesAsync(MiaoDbContext db, Guid characterId, string multilineValue)
        {
            var wantedLines = multilineValue
                .Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .Distinct()
                .ToList();

            var existing = await db.CharacterAliases.Where(a => a.CharacterId == characterId).ToListAsync();

            var toRemove = existing.Where(a => !wantedLines.Contains(a.AliasText)).ToList();
            db.CharacterAliases.RemoveRange(toRemove);

            var skipped = new List<string>();
            var currentTexts = existing.Select(a => a.AliasText).Except(toRemove.Select(a => a.AliasText)).ToHashSet();

            foreach (var line in wantedLines)
            {
                if (currentTexts.Contains(line)) continue;

                var normalized = VietnameseTextNormalizer.Normalize(line);
                bool duplicateElsewhere = await db.CharacterAliases
                    .Where(a => a.CharacterId != characterId && a.IsEnabledForScan)
                    .AnyAsync(a => a.NormalizedAliasText == normalized);

                if (duplicateElsewhere) { skipped.Add(line); continue; }

                db.CharacterAliases.Add(new CharacterAlias
                {
                    CharacterId = characterId,
                    AliasText = line,
                    NormalizedAliasText = normalized
                });
            }

            await db.SaveChangesAsync();
            CharacterScanCache.InvalidateAll();
            return skipped;
        }

        public static Task<List<CharacterDescriptionSection>> GetDescriptionSectionsAsync(MiaoDbContext db, Guid characterId) =>
            db.CharacterDescriptionSections
                .Include(s => s.Blocks.OrderBy(b => b.SortOrder))
                .Where(s => s.CharacterId == characterId)
                .OrderBy(s => s.SortOrder)
                .ToListAsync();

        public static async Task<CharacterDescriptionSection> AddDescriptionSectionAsync(MiaoDbContext db, Guid characterId, string title)
        {
            var count = await db.CharacterDescriptionSections.CountAsync(s => s.CharacterId == characterId);
            var section = new CharacterDescriptionSection { CharacterId = characterId, Title = title, SortOrder = count };
            db.CharacterDescriptionSections.Add(section);
            await db.SaveChangesAsync();
            return section;
        }

        public static async Task RenameDescriptionSectionAsync(MiaoDbContext db, Guid sectionId, string title)
        {
            var section = await db.CharacterDescriptionSections.FindAsync(sectionId);
            if (section == null) return;
            section.Title = title;
            await db.SaveChangesAsync();
        }

        public static async Task DeleteDescriptionSectionAsync(MiaoDbContext db, Guid sectionId)
        {
            var section = await db.CharacterDescriptionSections.FindAsync(sectionId);
            if (section == null) return;
            db.CharacterDescriptionSections.Remove(section);
            await db.SaveChangesAsync();
        }

        public static async Task<CharacterDescriptionBlock> InsertTextBlockAsync(MiaoDbContext db, Guid sectionId, int atIndex)
        {
            var block = new CharacterDescriptionBlock { CharacterDescriptionSectionId = sectionId, Type = DescriptionBlockType.Text, TextContent = "" };
            await InsertBlockAtAsync(db, sectionId, atIndex, block);
            return block;
        }

        public static async Task<CharacterDescriptionBlock> InsertImageBlockAsync(MiaoDbContext db, Guid sectionId, int atIndex, byte[] pngBytes)
        {
            var block = new CharacterDescriptionBlock { CharacterDescriptionSectionId = sectionId, Type = DescriptionBlockType.Image, Id = Guid.NewGuid() };
            db.CharacterDescriptionBlocks.Add(block);
            block.ImagePath = CharacterImageStorage.SaveDescriptionImageBytes(sectionId, block.Id, pngBytes);
            await InsertBlockAtAsync(db, sectionId, atIndex, block, alreadyTracked: true);
            return block;
        }

        private static async Task InsertBlockAtAsync(MiaoDbContext db, Guid sectionId, int atIndex, CharacterDescriptionBlock block, bool alreadyTracked = false)
        {
            var siblings = await db.CharacterDescriptionBlocks
                .Where(b => b.CharacterDescriptionSectionId == sectionId)
                .OrderBy(b => b.SortOrder)
                .ToListAsync();

            siblings.Insert(Math.Clamp(atIndex, 0, siblings.Count), block);
            for (int i = 0; i < siblings.Count; i++) siblings[i].SortOrder = i;

            if (!alreadyTracked) db.CharacterDescriptionBlocks.Add(block);
            await db.SaveChangesAsync();
        }

        public static async Task UpdateTextBlockAsync(MiaoDbContext db, Guid blockId, string text)
        {
            var block = await db.CharacterDescriptionBlocks.FindAsync(blockId);
            if (block == null) return;
            block.TextContent = text;
            await db.SaveChangesAsync();
        }

        public static async Task DeleteBlockAsync(MiaoDbContext db, Guid blockId)
        {
            var block = await db.CharacterDescriptionBlocks.FindAsync(blockId);
            if (block == null) return;
            db.CharacterDescriptionBlocks.Remove(block);
            await db.SaveChangesAsync();
        }
    }
}