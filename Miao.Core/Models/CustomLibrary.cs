using System;
using System.Collections.Generic;

namespace Miao.Core.Models
{
    public class CustomLibrary
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;

        public int SortOrder { get; set; }

        public List<CustomLibraryNovel> Items { get; set; } = new();
    }
}