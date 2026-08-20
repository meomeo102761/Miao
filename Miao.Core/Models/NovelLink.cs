using System;

namespace Miao.Core.Models
{
    public class NovelLink
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid NovelId { get; set; }
        public Novel? Novel { get; set; }

        public string Description { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }
}