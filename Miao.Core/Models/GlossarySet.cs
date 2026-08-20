using System;
using System.Collections.Generic;

namespace Miao.Core.Models
{
    public class GlossarySet
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public bool IsShared { get; set; }
        public int SortOrder { get; set; }

        // Chỉ có giá trị với bộ riêng — truyện nào "sở hữu" bộ này (thường là truyện
        // đã tự sinh ra nó lúc mới tạo). Khác với việc bộ đang được ÁP DỤNG cho
        // truyện nào (xem NovelGlossarySet) — 1 bộ riêng có thể được truyện khác mượn dùng.
        public Guid? OwnerNovelId { get; set; }
        public Novel? OwnerNovel { get; set; }

        public List<GlossarySetEntry> Entries { get; set; } = new();
    }
}