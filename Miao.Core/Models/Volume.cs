using System;

namespace Miao.Core.Models
{
    public class Volume
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid NovelId { get; set; }
        public string Name { get; set; } = "";

        public int SortOrder { get; set; }
    }
}