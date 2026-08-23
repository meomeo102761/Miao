using System.Text;

namespace Miao.Core.Services
{
    /// <summary>
    /// Giải mã text bị Fanqie che bằng vùng Private Use Area (PUA), dùng cho
    /// đường fallback không cần REG_KEY trong <see cref="FanqieDownloadSource"/>.
    /// Xem thêm giải thích cơ chế trong <see cref="FanqieCharset"/>.
    /// </summary>
    public static class FanqiePuaDecoder
    {
        public static string Decode(string rawText)
        {
            if (string.IsNullOrEmpty(rawText))
                return rawText;

            var sb =
                new StringBuilder(rawText.Length);

            foreach (var ch in rawText)
            {
                var code =
                    (int)ch;

                if (code >= FanqieCharset.CodeStart &&
                    code <= FanqieCharset.CodeEnd)
                {
                    var index =
                        code - FanqieCharset.CodeStart;

                    if (index >= 0 &&
                        index < FanqieCharset.Charset.Length)
                    {
                        sb.Append(
                            FanqieCharset.Charset[index]);

                        continue;
                    }
                }

                sb.Append(ch);
            }

            return sb.ToString();
        }
    }
}