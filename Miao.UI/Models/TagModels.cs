using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Miao.UI.Models
{
    public class TagCategoryGroup
    {
        public string Category { get; set; } = string.Empty;
        public bool IsStatusGroup { get; set; }
        public List<TagCheckItem> Tags { get; set; } = new();
    }

    public class TagCheckItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public Guid TagId { get; set; }
        public string Name { get; set; } = string.Empty;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public class LinkItem
    {
        public string Description { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }
}