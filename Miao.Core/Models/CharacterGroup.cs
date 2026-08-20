using System;
using System.Collections.Generic;

namespace Miao.Core.Models
{
    // "Dàn nhân vật" — vd. Naruto, One Piece — tương tự GlossarySet (chung/riêng)
    public class CharacterGroup
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public bool IsShared { get; set; }
        public int SortOrder { get; set; }

        // Truyện sở hữu bộ này (nếu là bộ riêng) — như OwnerNovelId của GlossarySet
        public Guid? OwnerNovelId { get; set; }
        public Novel? OwnerNovel { get; set; }

        public List<Character> Characters { get; set; } = new();
    }
}