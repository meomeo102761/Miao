using System;

namespace Miao.Core.Models
{
    public class CustomLibraryNovel
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid CustomLibraryId { get; set; }
        public CustomLibrary? CustomLibrary { get; set; }

        public Guid NovelId { get; set; }
        public Novel? Novel { get; set; }
    }
}