using System.Collections.Generic;

namespace Miao.Core.Services
{
    /// <summary>
    /// Sinh danh sách số trang hiển thị kiểu "1 2 3 4 ... 20": luôn có trang đầu, trang cuối,
    /// trang hiện tại và 1 trang liền kề mỗi bên; chỗ nào bị hụt thì chèn null (dấu "…").
    /// Dùng chung cho các trang có phân trang dạng số (AuthorListPage, AuthorPage, CustomLibraryDetailPage...).
    /// </summary>
    public static class PagerNumbers
    {
        public static List<int?> Build(int current, int total)
        {
            var pages = new SortedSet<int> { 1, total, current };
            for (var p = current - 1; p <= current + 1; p++)
                if (p >= 1 && p <= total) pages.Add(p);

            var result = new List<int?>();
            int? previous = null;
            foreach (var p in pages)
            {
                if (previous.HasValue && p - previous.Value > 1)
                    result.Add(null);
                result.Add(p);
                previous = p;
            }
            return result;
        }
    }
}