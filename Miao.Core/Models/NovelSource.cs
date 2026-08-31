using System;

namespace Miao.Core.Models
{
    public class NovelSource
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid NovelId { get; set; }
        public Novel? Novel { get; set; }

        public string SourceName { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public bool IsPrimary { get; set; } = false;
    }
}