using System;
using System.Collections.Generic;

namespace Miao.Core.Models
{
    public class CharacterGroup
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public bool IsShared { get; set; }
        public int SortOrder { get; set; }

        public string? CoverImagePath { get; set; }
        
        public double BannerFocalX { get; set; } = 0.5;
        public double BannerFocalY { get; set; } = 0.5;
        public double BannerScale { get; set; } = 0;

        public Guid? OwnerNovelId { get; set; }
        public Novel? OwnerNovel { get; set; }

        public List<Character> Characters { get; set; } = new();
    }
}