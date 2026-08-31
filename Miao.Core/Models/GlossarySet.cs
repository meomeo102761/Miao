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

        public Guid? OwnerNovelId { get; set; }
        public Novel? OwnerNovel { get; set; }

        public List<GlossarySetEntry> Entries { get; set; } = new();

        public List<GlossaryGroup> Groups { get; set; } = new();
    }
}