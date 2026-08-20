using System;
using Avalonia.Controls;

namespace Miao.UI.Services
{
    public static class ModalService
    {
        private static Panel? _overlay;
        private static ContentControl? _content;

        public static void Register(Panel overlay, ContentControl content)
        {
            _overlay = overlay;
            _content = content;
        }

        public static void Show(Control modalContent)
        {
            if (_overlay is null || _content is null)
                throw new InvalidOperationException("ModalService chưa được Register. Hãy gọi từ MainView trước.");

            _content.Content = modalContent;
            _overlay.IsVisible = true;
        }

        public static void Close()
        {
            if (_overlay is null)
                return;

            _overlay.IsVisible = false;
            if (_content is not null)
                _content.Content = null;
        }
    }
}