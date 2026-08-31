using System;
using System.Collections.Generic;

namespace Miao.Core.Models
{
    public class Character
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid CharacterGroupId { get; set; }
        public CharacterGroup? CharacterGroup { get; set; }

        public string Name { get; set; } = string.Empty;

        public string ImagePath { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public Guid? FactionId { get; set; }
        public CharacterFaction? Faction { get; set; }

        public List<CharacterAlias> Aliases { get; set; } = new();
    }
}