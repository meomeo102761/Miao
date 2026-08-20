using System;

namespace Miao.Core.Models
{
    public class NovelTag
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid NovelId { get; set; }
        public Novel? Novel { get; set; }

        public Guid TagId { get; set; }
        public Tag? Tag { get; set; }
    }
}