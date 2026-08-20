using System;

namespace Miao.Core.Models
{
    public class NoteEntry
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ChapterId { get; set; }
        public Chapter? Chapter { get; set; }

        public string Content { get; set; } = string.Empty;
    }
}