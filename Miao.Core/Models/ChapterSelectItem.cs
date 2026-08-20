namespace Miao.Core.Models
{
    public class ChapterSelectItem
    {
        public bool IsSelected { get; set; } = true;
        public int Number { get; set; }
        public string Title { get; set; } = string.Empty;
        public string ChapterUrl { get; set; } = string.Empty;
    }
}