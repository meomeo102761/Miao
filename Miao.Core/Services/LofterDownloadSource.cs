using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace Miao.Core.Services
{
    public class LofterDownloadSource : IDownloadSource
    {
        public string SourceName => "lofter";
        public bool UsesSourceChapterNumbers => false;

        private readonly HttpClient _http = new();
        private readonly Dictionary<string, string> _contentCache = new();
        private readonly Dictionary<string, List<string>> _tagsCache = new();
        public IReadOnlyDictionary<string, List<string>> ChapterTags => _tagsCache;

        public LofterDownloadSource()
        {
            _http.DefaultRequestHeaders.Add("User-Agent", "LOFTER-iOS 10.0.0");
        }

        public bool CanHandle(string url) => url.Contains(".lofter.com");

        private string ExtractBlogDomain(string url)
        {
            var match = Regex.Match(url, @"https?://([a-zA-Z0-9\-]+)\.lofter\.com");
            if (!match.Success)
                throw new Exception("Không đọc được tên blog từ link — cần dạng https://tenuser.lofter.com/...");
            return $"{match.Groups[1].Value}.lofter.com";
        }

        private async Task<List<JsonElement>> FetchAllPostsAsync(string blogDomain)
        {
            const int pageSize = 200;
            var all = new List<JsonElement>();
            var offset = 0;

            while (true)
            {
                var page = await FetchPostsPageAsync(blogDomain, offset, pageSize);
                if (page.GetArrayLength() == 0) break;

                foreach (var item in page.EnumerateArray())
                    all.Add(item.Clone()); 

                if (page.GetArrayLength() < pageSize) break;
                offset += pageSize;
            }

            return all;
        }

        private async Task<JsonElement> FetchPostsPageAsync(string blogDomain, int offset, int limit)
        {
            var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["blogdomain"] = blogDomain,
                ["checkpwd"] = "1",
                ["following"] = "0",
                ["limit"] = limit.ToString(),
                ["method"] = "getPostLists",
                ["needgetpoststat"] = "1",
                ["offset"] = offset.ToString(),  
                ["postdigestnew"] = "1",
                ["supportposttypes"] = "1,2,3,4,5,6"
            });

            var response = await _http.PostAsync(
                "http://api.lofter.com/v2.0/blogHomePage.api?product=lofter-iphone-10.0.0", body);
            var json = await response.Content.ReadAsStringAsync();

            var root = JsonDocument.Parse(json).RootElement;
            if (!root.TryGetProperty("response", out var resp) || !resp.TryGetProperty("posts", out var posts))
                throw new Exception("Không tìm thấy blog này hoặc blog trống.");

            return posts;
        }

        public async Task<(string Title, string Author, string CoverImageUrl, string Description)> GetNovelInfoAsync(string url)
        {
            var blogDomain = ExtractBlogDomain(url);
            var posts = await FetchPostsPageAsync(blogDomain, offset: 0, limit: 1); 

            if (posts.GetArrayLength() == 0) return ("", "", "", "");

            var firstPost = posts[0].GetProperty("post");
            var blogInfo = firstPost.GetProperty("blogInfo");
            var author = blogInfo.GetProperty("blogNickName").GetString() ?? "";

            return (author, author, "", "");
        }

        public async Task<List<(int, string, string)>> GetChapterListAsync(string url)
        {
            var blogDomain = ExtractBlogDomain(url);
            var posts = await FetchAllPostsAsync(blogDomain);  

            var result = new List<(int, string, string)>();
            var rawList = new List<(string Title, string Link, string Content, string PhotoLinks, long PublishTime, string TagStr)>();

            foreach (var item in posts)  
            {
                var post = item.GetProperty("post");
                var title = post.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(title))
                    title = post.TryGetProperty("noticeLinkTitle", out var nlt) ? nlt.GetString() ?? "(Không tiêu đề)" : "(Không tiêu đề)";

                var link = post.GetProperty("blogPageUrl").GetString() ?? "";
                var content = post.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                var photoLinks = post.TryGetProperty("photoLinks", out var pl) ? pl.GetString() ?? "" : "";
                var publishTime = post.TryGetProperty("publishTime", out var pt) ? pt.GetInt64() : 0;
                var tagStr = post.TryGetProperty("tag", out var tg) ? tg.GetString() ?? "" : "";

                rawList.Add((title, link, content, photoLinks, publishTime, tagStr));
            }

            rawList = rawList.OrderBy(x => x.PublishTime).ToList();

            int internalNumber = 1;
            foreach (var (title, link, content, photoLinks, _, tagStr) in rawList)
            {
                var sb = new System.Text.StringBuilder();

                var textPart = HtmlToPlainText(content);
                if (!string.IsNullOrWhiteSpace(textPart))
                {
                    sb.AppendLine(textPart);
                    sb.AppendLine();
                }

                foreach (var photoUrl in ExtractPhotoUrls(photoLinks))
                {
                    sb.AppendLine($"[[IMG:{photoUrl}]]");
                    sb.AppendLine();
                }

                _tagsCache[link] = tagStr
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

                _contentCache[link] = sb.ToString().Trim();
                result.Add((internalNumber++, title, link));
            }

            return result;
        }

        public Task<string> GetChapterContentAsync(string chapterUrl)
        {
            return Task.FromResult(_contentCache.TryGetValue(chapterUrl, out var content) ? content : "");
        }

        private static List<string> ExtractPhotoUrls(string? photoLinksJson)
        {
            var urls = new List<string>();
            if (string.IsNullOrWhiteSpace(photoLinksJson)) return urls;

            try
            {
                using var doc = JsonDocument.Parse(photoLinksJson);
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    string? url = null;
                    if (item.TryGetProperty("orignWithTag", out var o) && o.ValueKind == JsonValueKind.String)
                        url = o.GetString();
                    else if (item.TryGetProperty("raw", out var r) && r.ValueKind == JsonValueKind.String)
                        url = r.GetString();
                    else if (item.TryGetProperty("middle", out var m) && m.ValueKind == JsonValueKind.String)
                        url = m.GetString();

                    if (!string.IsNullOrWhiteSpace(url))
                        urls.Add(url);
                }
            }
            catch
            {
               
            }

            return urls;
        }

        private string HtmlToPlainText(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return "";

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var sb = new System.Text.StringBuilder();
            AppendNodeContent(doc.DocumentNode, sb);
            return sb.ToString().Trim();
        }

        private void AppendNodeContent(HtmlNode node, System.Text.StringBuilder sb)
        {
            foreach (var child in node.ChildNodes)
            {
                if (child.Name.Equals("img", StringComparison.OrdinalIgnoreCase))
                {
                    var src = child.GetAttributeValue("src", "").Trim();
                    if (!string.IsNullOrWhiteSpace(src))
                    {
                        sb.AppendLine($"[[IMG:{src}]]");
                        sb.AppendLine();
                    }
                    continue;
                }

                if (child.NodeType == HtmlNodeType.Text)
                {
                    var text = HtmlEntity.DeEntitize(child.InnerText).Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        sb.AppendLine(text);
                        sb.AppendLine();
                    }
                    continue;
                }

                if (child.HasChildNodes)
                    AppendNodeContent(child, sb);
            }
        }
    }
}