using System;

namespace Miao.Core.Models
{
    // Bảng nối = "Dàn nhân vật đang được BẬT cho truyện này khi đọc"
    // Tương tự NovelGlossarySet — 1 truyện có thể bật nhiều dàn nhân vật cùng lúc
    public class NovelCharacterGroup
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid NovelId { get; set; }
        public Novel? Novel { get; set; }

        public Guid CharacterGroupId { get; set; }
        public CharacterGroup? CharacterGroup { get; set; }
    }
}