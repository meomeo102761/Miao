using System;
using System.Collections.Generic;

namespace Miao.Core.Models
{
    public class CharacterInfoSection
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CharacterId { get; set; }
        public Character? Character { get; set; }
        public string Title { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public List<CharacterInfoEntry> Entries { get; set; } = new();
    }
}