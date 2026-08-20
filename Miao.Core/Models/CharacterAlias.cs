using System;

namespace Miao.Core.Models
{
    // Mỗi dòng là 1 cách gọi tên khác của nhân vật (tên gốc, Hán Việt, biệt danh...)
    // Dùng để quét text lúc đọc, phát hiện tên trùng để hiện ảnh nhân vật khi bấm vào
    public class CharacterAlias
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid CharacterId { get; set; }
        public Character? Character { get; set; }

        public string AliasText { get; set; } = string.Empty;
    }
}