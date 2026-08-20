using System;

namespace Miao.Core.Models
{
    public class GlossarySetEntry
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid GlossarySetId { get; set; }
        public GlossarySet? GlossarySet { get; set; }

        public string OriginalTerm { get; set; } = string.Empty;   // Gốc
        public string? HanViet { get; set; }                        // Hán Việt
        public string? PinYin { get; set; }                          // Bính Âm
        public string TranslatedTerm { get; set; } = string.Empty; // Name
    }
}