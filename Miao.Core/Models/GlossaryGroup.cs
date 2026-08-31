using System;
using System.Collections.Generic;

namespace Miao.Core.Models
{
    public class GlossaryGroup
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public int SortOrder { get; set; }

        public bool IsShared { get; set; }

        public List<GlossarySet> Sets { get; set; } = new();
    }
}