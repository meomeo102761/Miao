using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Miao.UI.Views.Pages
{
    public class InlineImageCropper : StackPanel
    {
        private const double DefaultViewportWidth = 320;
        private const double DefaultViewportHeight = 300;
        private const double ExportScale = 2.0;

        private readonly double _viewportWidth;
        private readonly double _viewportHeight;

        private Bitmap? _source;
        private readonly Image _image;
        private readonly Border _viewport;
        private readonly Slider _zoomSlider;

        private double _minScale, _scale, _offsetX, _offsetY;
        private Point _dragStart;
        private double _dragStartOffsetX, _dragStartOffsetY;
        private bool _dragging;

        public bool HasImage => _source != null;

        public InlineImageCropper() : this(DefaultViewportWidth, DefaultViewportHeight) { }

        public InlineImageCropper(double viewportWidth, double viewportHeight)
        {
            _viewportWidth = viewportWidth;
            _viewportHeight = viewportHeight;

            Spacing = 8;

            _image = new Image { Stretch = Stretch.Fill };
            var canvas = new Canvas { Width = _viewportWidth, Height = _viewportHeight };
            canvas.Children.Add(_image);

            _viewport = new Border
            {
                Width = _viewportWidth, Height = _viewportHeight, ClipToBounds = true,
                Background = Brushes.LightGray, CornerRadius = new CornerRadius(8),
                Child = canvas, Cursor = new Cursor(StandardCursorType.SizeAll)
            };
            _viewport.PointerPressed += OnPressed;
            _viewport.PointerMoved += OnMoved;
            _viewport.PointerReleased += OnReleased;

            _zoomSlider = new Slider { Width = _viewportWidth, IsEnabled = false };
            _zoomSlider.ValueChanged += (_, e) => ApplyZoom(e.NewValue);

            Children.Add(_viewport);
            Children.Add(_zoomSlider);
        }

        public void SetSource(string filePath) => SetSource(new Bitmap(filePath));

        public void SetSource(Bitmap bitmap)
        {
            _source = bitmap;
            var srcW = bitmap.PixelSize.Width;
            var srcH = bitmap.PixelSize.Height;
            _minScale = Math.Max(_viewportWidth / srcW, _viewportHeight / srcH);
            _scale = _minScale;
            _offsetX = (_viewportWidth - srcW * _scale) / 2;
            _offsetY = (_viewportHeight - srcH * _scale) / 2;

            _zoomSlider.Minimum = _minScale;
            _zoomSlider.Maximum = _minScale * 3;
            _zoomSlider.IsEnabled = true;
            _zoomSlider.Value = _minScale;

            ApplyTransform();
        }

        private void ApplyZoom(double newScale)
        {
            if (_source == null) return;
            var cx = _viewportWidth / 2;
            var cy = _viewportHeight / 2;
            var imgX = (cx - _offsetX) / _scale;
            var imgY = (cy - _offsetY) / _scale;

            _scale = newScale;
            _offsetX = cx - imgX * _scale;
            _offsetY = cy - imgY * _scale;
            ClampOffsets();
            ApplyTransform();
        }

        private void ApplyTransform()
        {
            _image.Source = _source;
            if (_source == null) return;
            _image.Width = _source.PixelSize.Width * _scale;
            _image.Height = _source.PixelSize.Height * _scale;
            Canvas.SetLeft(_image, _offsetX);
            Canvas.SetTop(_image, _offsetY);
        }

        private void OnPressed(object? sender, PointerPressedEventArgs e)
        {
            if (_source == null) return;
            _dragging = true;
            _dragStart = e.GetPosition(_viewport);
            _dragStartOffsetX = _offsetX;
            _dragStartOffsetY = _offsetY;
            e.Pointer.Capture(_viewport);
        }

        private void OnMoved(object? sender, PointerEventArgs e)
        {
            if (!_dragging) return;
            var pos = e.GetPosition(_viewport);
            _offsetX = _dragStartOffsetX + (pos.X - _dragStart.X);
            _offsetY = _dragStartOffsetY + (pos.Y - _dragStart.Y);
            ClampOffsets();
            ApplyTransform();
        }

        private void OnReleased(object? sender, PointerReleasedEventArgs e)
        {
            _dragging = false;
            e.Pointer.Capture(null);
        }

        private void ClampOffsets()
        {
            if (_source == null) return;
            var scaledW = _source.PixelSize.Width * _scale;
            var scaledH = _source.PixelSize.Height * _scale;
            _offsetX = Math.Min(0, Math.Max(_offsetX, _viewportWidth - scaledW));
            _offsetY = Math.Min(0, Math.Max(_offsetY, _viewportHeight - scaledH));
        }

        public byte[] GetCroppedPngBytes()
        {
            if (_source == null)
                return Array.Empty<byte>();

            const double exportScale = 2.0;
            var exportW = _viewportWidth * exportScale;
            var exportH = _viewportHeight * exportScale;

            var exportImage = new Image
            {
                Source = _source,
                Width = _source.PixelSize.Width * _scale * exportScale,
                Height = _source.PixelSize.Height * _scale * exportScale,
                Stretch = Stretch.Fill
            };
            var exportCanvas = new Canvas { Width = exportW, Height = exportH };
            exportCanvas.Children.Add(exportImage);
            Canvas.SetLeft(exportImage, _offsetX * exportScale);
            Canvas.SetTop(exportImage, _offsetY * exportScale);

            var exportBorder = new Border { Width = exportW, Height = exportH, ClipToBounds = true, Child = exportCanvas };

            var pixelSize = new PixelSize((int)exportW, (int)exportH);
            var target = new RenderTargetBitmap(pixelSize, new Vector(96, 96)); 

            exportBorder.Measure(new Size(exportW, exportH));
            exportBorder.Arrange(new Rect(0, 0, exportW, exportH));
            target.Render(exportBorder);

            using var ms = new MemoryStream();
        #pragma warning disable CS0618
            target.Save(ms);
        #pragma warning restore CS0618
            return ms.ToArray();
        }
    }
}