using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace Miao.Core.Services
{
    public abstract class GenericNovelDownloadSource : IDownloadSource
    {
        protected readonly IPageFetcher _fetcher;

        protected GenericNovelDownloadSource(IPageFetcher fetcher)
        {
            _fetcher = fetcher;
        }

        public abstract string SourceName { get; }

        public abstract bool CanHandle(string url);

        public virtual bool ProvidesTranslatedContent => false;

        // =========================
        // Lấy HTML
        // =========================

        protected async Task<HtmlDocument> GetDocumentAsync(string url)
        {
            var html = await _fetcher.FetchHtmlAsync(url);

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            return doc;
        }

        // =========================
        // Thông tin truyện
        // =========================

        protected abstract string GetTitle(HtmlDocument doc);

        protected abstract string GetAuthor(HtmlDocument doc);

        protected abstract string GetCoverImageUrl(HtmlDocument doc, string pageUrl);

        protected virtual string GetDescription(HtmlDocument doc)
        {
            return "";
        }

        public async Task<(string Title, string Author, string CoverImageUrl, string Description)>
            GetNovelInfoAsync(string url)
        {
            var doc = await GetDocumentAsync(url);

            var title = GetTitle(doc);
            var author = GetAuthor(doc);
            var cover = GetCoverImageUrl(doc, url);
            var description = GetDescription(doc);

            return (
                title?.Trim() ?? "",
                author?.Trim() ?? "",
                cover?.Trim() ?? "",
                description?.Trim() ?? ""
            );
        }

        // =========================
        // Danh sách chương
        // =========================

        protected abstract IEnumerable<HtmlNode> GetChapterNodes(HtmlDocument doc);

        protected virtual string GetChapterTitle(HtmlNode node)
        {
            return node.InnerText.Trim();
        }

        protected virtual string GetChapterUrl(HtmlNode node, string pageUrl)
        {
            var url = node.GetAttributeValue("href", "");
            return MakeAbsoluteUrl(pageUrl, url);
        }

        public async Task<List<(int Number, string Title, string ChapterUrl)>>
            GetChapterListAsync(string url)
        {
            var doc = await GetDocumentAsync(url);

            var result = new List<(int Number, string Title, string ChapterUrl)>();

            var nodes = GetChapterNodes(doc);

            int number = 1;

            foreach (var node in nodes)
            {
                var title = GetChapterTitle(node);
                var chapterUrl = GetChapterUrl(node, url);

                if (string.IsNullOrWhiteSpace(chapterUrl))
                    continue;

                result.Add((number++, title, chapterUrl));
            }

            return result;
        }

        // =========================
        // Nội dung chương
        // =========================

        protected abstract HtmlNode? GetChapterContentNode(HtmlDocument doc);

        protected virtual string ExtractChapterContent(HtmlNode node)
        {
            return HtmlContentExtractor.ExtractTextWithImages(node);
        }

        public async Task<string> GetChapterContentAsync(string chapterUrl)
        {
            var doc = await GetDocumentAsync(chapterUrl);

            var contentNode = GetChapterContentNode(doc);

            if (contentNode == null)
                return "";

            return ExtractChapterContent(contentNode);
        }

        // =========================
        // URL
        // =========================

        protected static string MakeAbsoluteUrl(string baseUrl, string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return "";

            if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
                return absolute.ToString();

            if (Uri.TryCreate(new Uri(baseUrl), url, out var combined))
                return combined.ToString();

            return url;
        }

        protected static string GetAttribute(HtmlNode node, string attribute)
        {
            return node?.GetAttributeValue(attribute, "") ?? "";
        }

        protected static string GetInnerText(HtmlNode node)
        {
            return node?.InnerText.Trim() ?? "";
        }
    }
}