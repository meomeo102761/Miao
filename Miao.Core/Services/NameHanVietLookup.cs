using System;
using System.Threading.Tasks;

namespace Miao.Core.Services
{
    /// <summary>
    /// Cung cấp 1 <see cref="DictionaryTranslationProvider"/> DÙNG CHUNG cho
    /// toàn app để tra Hán Việt theo cụm tên riêng (xem
    /// <see cref="DictionaryTranslationProvider.ToHanVietPhraseAsync"/>).
    /// Tránh việc mỗi trang (GlossaryPage, NovelDetailPage, ReaderPage...) tự
    /// tạo 1 instance riêng rồi load lại ~1 triệu dòng của Name.json/VP.json.
    /// </summary>
    public static class NameHanVietLookup
    {
        private static readonly Lazy<DictionaryTranslationProvider> Shared =
            new(() => new DictionaryTranslationProvider());

        /// <summary>
        /// Trả về Hán Việt của <paramref name="text"/>, ưu tiên khớp nguyên cụm
        /// tên riêng trong Name.json trước, phần còn lại fallback từng chữ qua
        /// HanViet.json. Lần gọi đầu tiên sẽ mất chút thời gian để load dictionary
        /// (~1 triệu dòng) — các lần sau dùng lại cache, gần như tức thì.
        /// </summary>
        public static async Task<string> ToHanVietAsync(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text ?? "";

            try
            {
                return await Shared.Value.ToHanVietPhraseAsync(text);
            }
            catch
            {
                // Dictionary lỗi/thiếu file — để caller tự fallback sang
                // SinoVietnameseConverter (đã có sẵn ở các trang gọi hàm này).
                return "";
            }
        }
    }
}