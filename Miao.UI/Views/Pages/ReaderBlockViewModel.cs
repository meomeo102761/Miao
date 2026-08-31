using System;
using System.ComponentModel;

namespace Miao.UI.Views.Pages.Reader
{
    public class ReaderBlockViewModel : INotifyPropertyChanged
    {
        public Guid BlockId { get; } = Guid.NewGuid();

        public ReaderBlockType Type { get; set; }

        private string _text = "";
        public string Text { get => _text; set { _text = value; OnChanged(nameof(Text)); } }

        public string ImagePath { get; set; } = "";

        private bool _isEditing;
        public bool IsEditing { get => _isEditing; set { _isEditing = value; OnChanged(nameof(IsEditing)); OnChanged(nameof(ShowTextEdit)); OnChanged(nameof(ShowTextView)); } }

        public bool IsImage => Type == ReaderBlockType.Image;
        public bool ShowTextView => Type == ReaderBlockType.Text && !IsEditing;
        public bool ShowTextEdit => Type == ReaderBlockType.Text && IsEditing;

        public static ReaderBlockViewModel FromBlock(ReaderBlock block, bool isEditing) => new()
        {
            Type = block.Type,
            Text = block.Text,
            ImagePath = block.ImagePath,
            IsEditing = isEditing
        };

        public ReaderBlock ToBlock() => new()
        {
            Type = Type,
            Text = Text,
            ImagePath = ImagePath
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
