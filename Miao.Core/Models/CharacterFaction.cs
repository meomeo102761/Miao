using System;

namespace Miao.Core.Models
{
    public class CharacterFaction
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CharacterGroupId { get; set; }
        public CharacterGroup? CharacterGroup { get; set; }
        public string Name { get; set; } = string.Empty;
        public int SortOrder { get; set; }
    }
}