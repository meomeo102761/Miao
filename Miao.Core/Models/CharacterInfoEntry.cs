using System;

namespace Miao.Core.Models
{
    public class CharacterInfoEntry
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CharacterInfoSectionId { get; set; }
        public CharacterInfoSection? Section { get; set; }
        public string Label { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public int SortOrder { get; set; }
    }
}