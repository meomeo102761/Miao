using System.Collections.Concurrent;
using Miao.Core.Data;
using Miao.Core.TextScanning;
using Microsoft.EntityFrameworkCore;

namespace Miao.Core.Services
{
    public static class CharacterScanCache
    {
        private static readonly ConcurrentDictionary<Guid, AhoCorasickAutomaton> _cache = new();

        public static void InvalidateNovel(Guid novelId) => _cache.TryRemove(novelId, out _);
        public static void InvalidateAll() => _cache.Clear();

        public static async Task<AhoCorasickAutomaton> GetOrBuildAsync(MiaoDbContext db, Guid novelId)
        {
            if (_cache.TryGetValue(novelId, out var cached))
                return cached;

            var scopeIds = await CharacterService.GetEffectiveScanScopeCharacterIdsAsync(db, novelId);

            var entries = await db.CharacterAliases
                .Where(a => a.IsEnabledForScan && scopeIds.Contains(a.CharacterId))
                .Select(a => new AliasEntry(a.NormalizedAliasText, a.CharacterId, a.Id))
                .ToListAsync();

            var automaton = AhoCorasickAutomaton.Build(entries);
            _cache[novelId] = automaton;
            return automaton;
        }
    }
}