using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace Miao.Core.Services
{
    public class JinjiangDownloadSource : IDownloadSource
    {
        public string SourceName => "jinjiang";

        private readonly IPageFetcher _fetcher;

        public JinjiangDownloadSource(IPageFetcher fetcher)
        {
            _fetcher = fetcher;
        }

        public bool CanHandle(string url) =>
            url.Contains("jjwxc.net", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("jjwxc.com", StringComparison.OrdinalIgnoreCase);

        private static readonly string[] BoilerplatePatterns =
        {
            "晋江文学城", "投诉", "举报", "收藏", "打赏", "作者有话说",
            "本作品来自互联网", "版权均为原创者所有", "分享到", "上一章", "下一章",
        };

        private async Task<HtmlDocument> LoadAsync(string url)
        {
            var html = await _fetcher.FetchHtmlAsync(url);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            return doc;
        }

        private static string ExtractNovelId(string url)
        {
            var match = Regex.Match(url, @"novelid=(\d+)");
            return match.Success ? match.Groups[1].Value : "";
        }

        public async Task<(string Title, string Author, string CoverImageUrl, string Description)> GetNovelInfoAsync(string url)
        {
            var doc = await LoadAsync(url);

            var pageTitle = doc.DocumentNode.SelectSingleNode("//title")?.InnerText ?? "";
            var titleMatch = Regex.Match(pageTitle, "《(.+?)》");
            var title = titleMatch.Success
                ? HtmlEntity.DeEntitize(titleMatch.Groups[1].Value).Trim()
                : "";

            if (string.IsNullOrWhiteSpace(title))
            {
                title = HtmlEntity.DeEntitize(
                    doc.DocumentNode.SelectSingleNode("//h1")?.InnerText ?? ""
                ).Trim();
            }

            // ================== Tác giả ==================

            var authorNode = doc.DocumentNode.SelectSingleNode(
                "//a[contains(@href,'oneauthor.php')]");

            var author = HtmlEntity.DeEntitize(
                authorNode?.InnerText ?? ""
            ).Trim();

            // ================== Bìa ==================

            var coverNode = doc.DocumentNode.SelectSingleNode(
                "//img[contains(@class,'noveldefaultimage')]"
            );

            var cover = "";

            if (coverNode != null)
            {
                // src là ảnh bìa thực tế đang được trang hiển thị
                cover = coverNode.GetAttributeValue("src", "");

                // Chỉ fallback sang _src nếu src không có
                if (string.IsNullOrWhiteSpace(cover))
                    cover = coverNode.GetAttributeValue("_src", "");

                if (!string.IsNullOrWhiteSpace(cover))
                    cover = MakeAbsolute(
                        url,
                        HtmlEntity.DeEntitize(cover).Trim()
                    );
            }

            // ================== Mô tả ==================

            var descriptionNode = doc.DocumentNode.SelectSingleNode(
                "//div[@id='novelintro']");

            var description = "";

            if (descriptionNode != null)
            {
                description = HtmlContentExtractor.ExtractTextWithImages(
                    descriptionNode,
                    Array.Empty<string>());
            }

            return (title, author, cover, description);
        }

        public async Task<List<(int Number, string Title, string ChapterUrl)>> GetChapterListAsync(string url)
        {
            var doc = await LoadAsync(url);
            var result = new List<(int, string, string)>();

            var novelId = ExtractNovelId(url);
            if (string.IsNullOrEmpty(novelId)) return result;

            var linkNodes = doc.DocumentNode.SelectNodes("//a[@href]");
            if (linkNodes == null) return result;

            var chapterPattern = new Regex($@"novelid={novelId}&(?:amp;)?chapterid=(\d+)");
            int index = 1;

            foreach (var node in linkNodes)
            {
                var href = node.GetAttributeValue("href", "");
                var match = chapterPattern.Match(href);
                if (!match.Success) continue;

                var title = HtmlEntity.DeEntitize(node.InnerText).Trim();
                if (string.IsNullOrWhiteSpace(title)) continue;

                var chapterUrl = $"https://www.jjwxc.net/onebook.php?novelid={novelId}&chapterid={match.Groups[1].Value}";
                result.Add((index++, title, chapterUrl));
            }

            return result;
        }

        public async Task<string> GetChapterContentAsync(string chapterUrl)
        {
            var doc = await LoadAsync(chapterUrl);
            var candidates = doc.DocumentNode.SelectNodes("//div | //p");
            if (candidates == null) return "";

            HtmlNode? best = null;
            int bestScore = 0;

            foreach (var node in candidates)
            {
                var text = HtmlEntity.DeEntitize(node.InnerText);
                var score = CountHanCharacters(text);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = node;
                }
            }

            if (best == null || bestScore < 30) return "";

            return HtmlContentExtractor.ExtractTextWithImages(best, BoilerplatePatterns);
        }

        private static int CountHanCharacters(string text)
        {
            int count = 0;
            foreach (var c in text)
                if (c >= 0x4E00 && c <= 0x9FFF) count++;
            return count;
        }

        private static string MakeAbsolute(string baseUrl, string href)
        {
            if (Uri.TryCreate(href, UriKind.Absolute, out var abs))
                return abs.ToString();

            if (Uri.TryCreate(new Uri(baseUrl), href, out var combined))
                return combined.ToString();

            return href;
        }
    }
}