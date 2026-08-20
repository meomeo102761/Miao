// using System.Collections.Generic;
// using HtmlAgilityPack;

// namespace Miao.Core.Services
// {
//     public class Novel543DownloadSource : GenericNovelDownloadSource
//     {
//         public override string SourceName => "Novel543";

//         public Novel543DownloadSource(IPageFetcher fetcher)
//             : base(fetcher)
//         {
//         }

//         public override bool CanHandle(string url)
//         {
//             return url.Contains("novel543.com");
//         }

//         // =========================
//         // TRANG GIỚI THIỆU TRUYỆN
//         // =========================

//         protected override string GetTitle(HtmlDocument doc)
//         {
//             // THAY XPath CỦA TÊN TRUYỆN VÀO ĐÂY
//             var node = doc.DocumentNode.SelectSingleNode(
//                 "XPATH_TEN_TRUYEN"
//             );

//             return GetInnerText(node);
//         }

//         protected override string GetAuthor(HtmlDocument doc)
//         {
//             // THAY XPath CỦA TÁC GIẢ VÀO ĐÂY
//             var node = doc.DocumentNode.SelectSingleNode(
//                 "XPATH_TAC_GIA"
//             );

//             return GetInnerText(node);
//         }

//         protected override string GetCoverImageUrl(
//             HtmlDocument doc,
//             string pageUrl)
//         {
//             // THAY XPath CỦA ẢNH BÌA VÀO ĐÂY
//             var node = doc.DocumentNode.SelectSingleNode(
//                 "XPATH_ANH_BIA"
//             );

//             var src = GetAttribute(node, "src");

//             return MakeAbsoluteUrl(pageUrl, src);
//         }

//         protected override string GetDescription(HtmlDocument doc)
//         {
//             // THAY XPath CỦA GIỚI THIỆU VÀO ĐÂY
//             var node = doc.DocumentNode.SelectSingleNode(
//                 "XPATH_GIOI_THIEU"
//             );

//             return GetInnerText(node);
//         }

//         // =========================
//         // TRANG /dir
//         // =========================

//         protected override IEnumerable<HtmlNode> GetChapterNodes(
//             HtmlDocument doc)
//         {
//             // THAY XPath CỦA CÁC LINK CHƯƠNG VÀO ĐÂY
//             return doc.DocumentNode.SelectNodes(
//                 "XPATH_DANH_SACH_CHUONG"
//             ) ?? [];
//         }

//         protected override string GetChapterTitle(HtmlNode node)
//         {
//             // Nếu text của <a> chính là tên chương
//             return node.InnerText.Trim();

//             // Nếu tên chương nằm trong span/div riêng
//             // thì đổi thành:
//             //
//             // var titleNode = node.SelectSingleNode(".//span");
//             // return GetInnerText(titleNode);
//         }

//         protected override string GetChapterUrl(
//             HtmlNode node,
//             string pageUrl)
//         {
//             var href = node.GetAttributeValue("href", "");

//             return MakeAbsoluteUrl(pageUrl, href);
//         }

//         // =========================
//         // TRANG CHƯƠNG
//         // =========================

//         protected override HtmlNode? GetChapterContentNode(
//             HtmlDocument doc)
//         {
//             // THAY XPath CỦA KHỐI NỘI DUNG CHƯƠNG VÀO ĐÂY
//             return doc.DocumentNode.SelectSingleNode(
//                 "XPATH_NOI_DUNG_CHUONG"
//             );
//         }
//     }
// }