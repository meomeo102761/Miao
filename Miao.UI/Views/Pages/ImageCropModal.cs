using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Miao.UI.Services;

namespace Miao.UI.Views.Pages
{
    public class ImageCropModal : Border
    {
        private const double ViewportWidth = 320;
        private const double ViewportHeight = 300;

        private readonly Bitmap _source;
        private readonly Action<Bitmap> _onDone;
        private readonly Image _image;
        private readonly Border _viewport;

        private readonly double _minScale;
        private double _scale;
        private double _offsetX, _offsetY;
        private Point _dragStart;
        private double _dragStartOffsetX, _dragStartOffsetY;
        private bool _dragging;

        public ImageCropModal(string sourceFilePath, Action<Bitmap> onDone)
        {
            _source = new Bitmap(sourceFilePath);
            _onDone = onDone;

            Width = ViewportWidth + 40;
            Background = Brushes.White;
            CornerRadius = new CornerRadius(12);
            Padding = new Thickness(20);

            _image = new Image { Stretch = Stretch.Fill };
            var canvas = new Canvas { Width = ViewportWidth, Height = ViewportHeight };
            canvas.Children.Add(_image);

            _viewport = new Border
            {
                Width = ViewportWidth,
                Height = ViewportHeight,
                ClipToBounds = true,
                Background = Brushes.Black,
                CornerRadius = new CornerRadius(6),
                Child = canvas,
                Cursor = new Cursor(StandardCursorType.SizeAll)
            };
            _viewport.PointerPressed += OnViewportPointerPressed;
            _viewport.PointerMoved += OnViewportPointerMoved;
            _viewport.PointerReleased += OnViewportPointerReleased;

            var srcW = _source.PixelSize.Width;
            var srcH = _source.PixelSize.Height;
            _minScale = Math.Max(ViewportWidth / srcW, ViewportHeight / srcH);
            _scale = _minScale;
            _offsetX = (ViewportWidth - srcW * _scale) / 2;
            _offsetY = (ViewportHeight - srcH * _scale) / 2;

            var zoomSlider = new Slider { Minimum = _minScale, Maximum = _minScale * 3, Value = _minScale, Width = ViewportWidth };
            zoomSlider.ValueChanged += (_, e) => ApplyZoom(e.NewValue);

            ApplyTransform();

            var doneButton = new Button { Content = "Xong", Classes = { "jade" } };
            doneButton.Click += (_, _) => Finish();
            var cancelButton = new Button { Content = "Hủy", Classes = { "outline" } };
            cancelButton.Click += (_, _) => ModalService.Close();

            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Kéo để chọn phần ảnh hiển thị", Classes = { "PageTitle" }, FontSize = 16, Margin = new Thickness(0) },
                    _viewport,
                    zoomSlider,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { cancelButton, doneButton } }
                }
            };
        }

        private void ApplyZoom(double newScale)
        {
            var cx = ViewportWidth / 2;
            var cy = ViewportHeight / 2;
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
            _image.Width = _source.PixelSize.Width * _scale;
            _image.Height = _source.PixelSize.Height * _scale;
            Canvas.SetLeft(_image, _offsetX);
            Canvas.SetTop(_image, _offsetY);
        }

        private void OnViewportPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            _dragging = true;
            _dragStart = e.GetPosition(_viewport);
            _dragStartOffsetX = _offsetX;
            _dragStartOffsetY = _offsetY;
            e.Pointer.Capture(_viewport);
        }

        private void OnViewportPointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_dragging) return;
            var pos = e.GetPosition(_viewport);
            _offsetX = _dragStartOffsetX + (pos.X - _dragStart.X);
            _offsetY = _dragStartOffsetY + (pos.Y - _dragStart.Y);
            ClampOffsets();
            ApplyTransform();
        }

        private void OnViewportPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            _dragging = false;
            e.Pointer.Capture(null);
        }

        private void ClampOffsets()
        {
            var scaledW = _source.PixelSize.Width * _scale;
            var scaledH = _source.PixelSize.Height * _scale;
            _offsetX = Math.Min(0, Math.Max(_offsetX, ViewportWidth - scaledW));
            _offsetY = Math.Min(0, Math.Max(_offsetY, ViewportHeight - scaledH));
        }

        private void Finish()
        {
            var pixelSize = new PixelSize((int)ViewportWidth, (int)ViewportHeight);
            var target = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
            _viewport.Measure(new Size(ViewportWidth, ViewportHeight));
            _viewport.Arrange(new Rect(0, 0, ViewportWidth, ViewportHeight));
            target.Render(_viewport);

            ModalService.Close();
            _onDone(target);
        }
    }
}