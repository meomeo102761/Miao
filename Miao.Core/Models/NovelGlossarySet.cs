using System;

namespace Miao.Core.Models
{
    public class NovelGlossarySet
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid NovelId { get; set; }
        public Novel? Novel { get; set; }

        public Guid GlossarySetId { get; set; }
        public GlossarySet? GlossarySet { get; set; }
    }
}