using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Miao.Core.Services
{
    /// <summary>
    /// Giải mã chữ bị thay bằng glyph font tùy biến (kiểu chống cào của Fanqie).
    ///
    /// Cách làm: font đính kèm mỗi chương ánh xạ 1 mã Unicode "giả" (thường nằm trong
    /// vùng Private Use Area, U+E000–U+F8FF) sang 1 hình glyph trông giống 1 chữ Hán
    /// thật. Ở đây so khớp HÌNH ẢNH: vẽ glyph giả ra bitmap bằng chính font đính kèm, CROP
    /// KHÍT theo vùng có nét vẽ rồi scale về cùng kích thước chuẩn hoá (để không nhạy cảm
    /// với lệch vị trí/kích thước giữa 2 font khác nhau), so với bitmap chuẩn hoá tương tự
    /// của ~1000 chữ Hán thường dùng (vẽ bằng font có sẵn trên Windows), chọn chữ giống nhất.
    /// </summary>
    public static class ObfuscatedFontDecoder
    {
        // Kích thước canvas lúc vẽ glyph ra (đủ lớn để không bị cắt nét).
        private const int DrawCanvasSize = 64;
        // Kích thước sau khi crop khít + scale chuẩn hoá — đây là kích thước thực sự đem so.
        private const int NormalizedSize = 32;
        private const string ReferenceFontName = "Microsoft YaHei";
        // Ngưỡng chấp nhận: tỉ lệ pixel khác nhau tối đa so với ứng viên tốt nhất.
        private const double MaxAcceptableDiffRatio = 0.30;

        private static readonly object _cacheLock = new();
        private static Dictionary<char, bool[]>? _referenceCache;

        public static string Decode(byte[] fontBytes, string rawText)
        {
            if (string.IsNullOrEmpty(rawText) || fontBytes == null || fontBytes.Length == 0)
                return rawText;

            var obfuscatedChars = rawText.Where(IsLikelyObfuscated).Distinct().ToList();
            if (obfuscatedChars.Count == 0)
                return rawText;

            EnsureReferenceCache();

            using var pfc = LoadPrivateFont(fontBytes);
            if (pfc == null || pfc.Families.Length == 0)
                return rawText;

            var mapping = new Dictionary<char, char>();
            using var customFont = new Font(pfc.Families[0], DrawCanvasSize * 0.8f, FontStyle.Regular, GraphicsUnit.Pixel);

            foreach (var ch in obfuscatedChars)
            {
                var mask = RenderNormalizedGlyphMask(ch, customFont);
                if (mask == null) continue;

                var best = FindClosestReferenceChar(mask);
                if (best != null)
                    mapping[ch] = best.Value;
            }

            if (mapping.Count == 0)
                return rawText;

            var sb = new StringBuilder(rawText.Length);
            foreach (var ch in rawText)
                sb.Append(mapping.TryGetValue(ch, out var replaced) ? replaced : ch);

            return sb.ToString();
        }

        private static bool IsLikelyObfuscated(char c) => c >= '\uE000' && c <= '\uF8FF';

        private static PrivateFontCollection? LoadPrivateFont(byte[] fontBytes)
        {
            try
            {
                var pfc = new PrivateFontCollection();
                var handle = GCHandle.Alloc(fontBytes, GCHandleType.Pinned);
                try
                {
                    pfc.AddMemoryFont(handle.AddrOfPinnedObject(), fontBytes.Length);
                }
                finally
                {
                    handle.Free();
                }
                return pfc;
            }
            catch
            {
                return null;
            }
        }

        /// Vẽ 1 ký tự ra bitmap lớn, tìm vùng có "mực" (nét vẽ), crop khít vùng đó rồi scale
        /// về đúng NormalizedSize x NormalizedSize. Nhờ vậy 2 font khác nhau vẽ cùng 1 chữ dù
        /// lệch vị trí/kích thước trên canvas gốc vẫn cho ra mask giống nhau để so sánh.
        private static bool[]? RenderNormalizedGlyphMask(char ch, Font font)
        {
            try
            {
                using var bmp = new Bitmap(DrawCanvasSize, DrawCanvasSize, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.DrawString(ch.ToString(), font, Brushes.Black,
                        new RectangleF(0, 0, DrawCanvasSize, DrawCanvasSize),
                        new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                }

                var rawMask = ToBoolGrid(bmp);
                var bounds = FindInkBounds(rawMask, DrawCanvasSize);
                if (bounds == null) return null; // canvas trắng hoàn toàn -> glyph rỗng/không vẽ được

                return CropAndScale(rawMask, DrawCanvasSize, bounds.Value);
            }
            catch
            {
                return null;
            }
        }

        private static bool[] ToBoolGrid(Bitmap bmp)
        {
            var mask = new bool[bmp.Width * bmp.Height];
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var stride = data.Stride;
                var bytes = new byte[stride * bmp.Height];
                Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);

                for (var y = 0; y < bmp.Height; y++)
                {
                    for (var x = 0; x < bmp.Width; x++)
                    {
                        var offset = y * stride + x * 4;
                        var brightness = bytes[offset]; // kênh B — ảnh chỉ đen/trắng nên dùng kênh nào cũng được
                        mask[y * bmp.Width + x] = brightness < 128;
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }
            return mask;
        }

        private static (int minX, int minY, int maxX, int maxY)? FindInkBounds(bool[] grid, int size)
        {
            int minX = size, minY = size, maxX = -1, maxY = -1;

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    if (!grid[y * size + x]) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            return maxX < 0 ? null : (minX, minY, maxX, maxY);
        }

        private static bool[] CropAndScale(bool[] grid, int size, (int minX, int minY, int maxX, int maxY) bounds)
        {
            var (minX, minY, maxX, maxY) = bounds;
            var srcW = maxX - minX + 1;
            var srcH = maxY - minY + 1;

            var result = new bool[NormalizedSize * NormalizedSize];

            // Giữ đúng tỉ lệ khung hình (không kéo méo chữ) — căn giữa trong khung vuông chuẩn hoá.
            var scale = (double)Math.Max(srcW, srcH);
            var offsetX = (NormalizedSize - srcW / scale * NormalizedSize) / 2.0;
            var offsetY = (NormalizedSize - srcH / scale * NormalizedSize) / 2.0;

            for (var ny = 0; ny < NormalizedSize; ny++)
            {
                for (var nx = 0; nx < NormalizedSize; nx++)
                {
                    var srcX = (int)((nx - offsetX) / NormalizedSize * scale) + minX;
                    var srcY = (int)((ny - offsetY) / NormalizedSize * scale) + minY;

                    if (srcX < minX || srcX > maxX || srcY < minY || srcY > maxY) continue;

                    result[ny * NormalizedSize + nx] = grid[srcY * size + srcX];
                }
            }

            return result;
        }

        private static void EnsureReferenceCache()
        {
            if (_referenceCache != null) return;

            lock (_cacheLock)
            {
                if (_referenceCache != null) return;

                var cache = new Dictionary<char, bool[]>();
                using var font = new Font(ReferenceFontName, DrawCanvasSize * 0.8f, FontStyle.Regular, GraphicsUnit.Pixel);

                foreach (var ch in CommonChars.Level1)
                {
                    var mask = RenderNormalizedGlyphMask(ch, font);
                    if (mask != null)
                        cache[ch] = mask;
                }

                _referenceCache = cache;
            }
        }

        private static char? FindClosestReferenceChar(bool[] target)
        {
            char? best = null;
            var bestDiff = int.MaxValue;

            foreach (var (ch, mask) in _referenceCache!)
            {
                var diff = HammingDistance(target, mask);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    best = ch;
                }
            }

            var maxAllowed = (int)(target.Length * MaxAcceptableDiffRatio);
            return bestDiff <= maxAllowed ? best : null;
        }

        private static int HammingDistance(bool[] a, bool[] b)
        {
            var diff = 0;
            for (var i = 0; i < a.Length; i++)
                if (a[i] != b[i]) diff++;
            return diff;
        }
    }
}