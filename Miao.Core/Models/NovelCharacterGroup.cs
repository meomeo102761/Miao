using System;

namespace Miao.Core.Models
{
    public class NovelCharacterGroup
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid NovelId { get; set; }
        public Novel? Novel { get; set; }

        public Guid CharacterGroupId { get; set; }
        public CharacterGroup? CharacterGroup { get; set; }
    }
}