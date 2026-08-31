using System;

namespace Miao.Core.Models
{
    public enum DescriptionBlockType { Text, Image }

    public class CharacterDescriptionBlock
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CharacterDescriptionSectionId { get; set; }
        public CharacterDescriptionSection? Section { get; set; }
        public DescriptionBlockType Type { get; set; }

        public string TextContent { get; set; } = string.Empty;

        public string? ImagePath { get; set; }

        public int SortOrder { get; set; }
    }
}