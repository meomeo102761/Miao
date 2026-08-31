using System;

namespace Miao.Core.Models
{
    public class GlossarySetEntry
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid GlossarySetId { get; set; }
        public GlossarySet? GlossarySet { get; set; }

        public string OriginalTerm { get; set; } = string.Empty;   
        public string? HanViet { get; set; }                        
        public string? PinYin { get; set; }                         
        public string TranslatedTerm { get; set; } = string.Empty;
    }
}