using System;

namespace Miao.Core.Models
{
    public class CharacterAlias
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid CharacterId { get; set; }
        public Character? Character { get; set; }

        public string AliasText { get; set; } = string.Empty;

        public string NormalizedAliasText { get; set; } = string.Empty;

        public bool IsEnabledForScan { get; set; } = true;
    }
}