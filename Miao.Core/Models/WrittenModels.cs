using System;
using System.IO;
using System.Linq;

namespace Miao.Core.Models
{
    public class WrittenNovel
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = "";
        public string Author { get; set; } = "";
        public string Description { get; set; } = "";
        public string Notes { get; set; } = "";
        public string Tags { get; set; } = "";
        public string CoverImagePath { get; set; } =
            Path.Combine(AppContext.BaseDirectory, "Assets", "default-cover.jpg");

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public string DisplayTitle =>
            string.IsNullOrWhiteSpace(Title) ? "Truyện Chưa Có Tiêu Đề" : Title;
    }

    public class WrittenChapter
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid NovelId { get; set; }
        public int Number { get; set; }
        public string Title { get; set; } = "";
        public string Content { get; set; } = "";
        public bool IsPublished { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public string DisplayTitle =>
            string.IsNullOrWhiteSpace(Title) ? $"Chưa đặt tiêu đề {Number}" : Title;

        public int WordCount => string.IsNullOrWhiteSpace(Content)
            ? 0
            : Content.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}