using System;

namespace Miao.Core.Models
{
    // Một "quyển" thuộc về 1 truyện, dùng để gom nhóm các chương lại.
    public class Volume
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid NovelId { get; set; }
        public string Name { get; set; } = "";

        // Thứ tự hiển thị các quyển (Quyển 1, Quyển 2, ...)
        public int SortOrder { get; set; }
    }
}