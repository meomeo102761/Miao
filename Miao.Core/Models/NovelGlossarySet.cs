using System;

namespace Miao.Core.Models
{
    // Bảng nối = "Bộ tên áp dụng": 1 dòng nghĩa là bộ tên này đang được bật cho truyện này.
    public class NovelGlossarySet
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid NovelId { get; set; }
        public Novel? Novel { get; set; }

        public Guid GlossarySetId { get; set; }
        public GlossarySet? GlossarySet { get; set; }
    }
}