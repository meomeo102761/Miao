using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Miao.Core.Data;
using Miao.Core.Models;

namespace Miao.Core.Services
{
    public static class CharacterFactionService
    {
        public static Task<List<CharacterFaction>> GetFactionsAsync(MiaoDbContext db, Guid groupId) =>
            db.CharacterFactions.Where(f => f.CharacterGroupId == groupId).OrderBy(f => f.SortOrder).ToListAsync();

        public static async Task AddFactionAsync(MiaoDbContext db, Guid groupId, string name)
        {
            var count = await db.CharacterFactions.CountAsync(f => f.CharacterGroupId == groupId);
            db.CharacterFactions.Add(new CharacterFaction { CharacterGroupId = groupId, Name = name, SortOrder = count });
            await db.SaveChangesAsync();
        }

        public static async Task RenameFactionAsync(MiaoDbContext db, Guid factionId, string name)
        {
            var faction = await db.CharacterFactions.FindAsync(factionId);
            if (faction == null) return;
            faction.Name = name;
            await db.SaveChangesAsync();
        }

        public static async Task DeleteFactionAsync(MiaoDbContext db, Guid factionId)
        {
            var faction = await db.CharacterFactions.FindAsync(factionId);
            if (faction == null) return;
            db.CharacterFactions.Remove(faction);
            await db.SaveChangesAsync();
        }

        public static async Task SetCharacterFactionAsync(MiaoDbContext db, Guid characterId, Guid? factionId)
        {
            var character = await db.Characters.FindAsync(characterId);
            if (character == null) return;
            character.FactionId = factionId;
            await db.SaveChangesAsync();
        }
    }
}