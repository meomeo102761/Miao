using System.Collections.Generic;
using System.ComponentModel;

namespace Miao.UI.Views.Pages;

public class SetOptionItem
{
    public System.Guid Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsApplied { get; set; }
}

public class ChapterListItem
{
    public int Number { get; set; }
    public string DisplayTitle { get; set; } = "";
    public System.Guid? VolumeId { get; set; }
    public bool IsRead { get; set; }
}

public class ChapterSection
{
    public System.Guid? VolumeId { get; set; }
    public string? Header { get; set; }
    public bool HasHeader => !string.IsNullOrEmpty(Header);
    public List<ChapterListItem> Chapters { get; set; } = new();
}

public class LofterUpdateItem : INotifyPropertyChanged
{
    private bool _isSelected = true;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnChanged(nameof(IsSelected)); }
    }

    private string _translatedTitle = "";
    public string TranslatedTitle
    {
        get => _translatedTitle;
        set { _translatedTitle = value; OnChanged(nameof(TranslatedTitle)); }
    }

    public string Title { get; set; } = "";
    public string ChapterUrl { get; set; } = "";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
