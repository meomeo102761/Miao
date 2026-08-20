using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Miao.Core.Data;
using Miao.Core.Services;
using Miao.Core.Models;
using Miao.UI.Services;

namespace Miao.UI.Views.Pages
{
    public class CustomLibrarySummary
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Count { get; set; }
    }

    public partial class CustomLibrariesPage : ConfirmablePage
    {
        private bool _isEditMode;
        private Point _dragStartPoint;
        private CustomLibrarySummary? _draggedItem;

        protected override Control ConfirmCardElement => ConfirmCard;
        protected override TextBlock ConfirmMessageTextElement => ConfirmMessageText;

        // Đổi tên riêng (Rename) không có sẵn trong ConfirmablePage vì đó là card khác ConfirmCard,
        // nên vẫn giữ show/hide RenameCard thủ công như code cũ.
        private CustomLibrarySummary? _renamingLibrary;

        public CustomLibrariesPage()
        {
            InitializeComponent();
            LoadLibraries();
        }

        private void LoadLibraries()
        {
            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            LibrariesList.ItemsSource = db.CustomLibraries
                .OrderBy(l => l.SortOrder)
                .ThenBy(l => l.Id)
                .Select(l => new CustomLibrarySummary
                {
                    Id = l.Id,
                    Name = l.Name,
                    Count = db.CustomLibraryNovels.Count(x => x.CustomLibraryId == l.Id)
                })
                .ToList();

            // Avalonia: Dispatcher.UIThread.Post thay cho Dispatcher.BeginInvoke của WPF,
            // vẫn chạy sau khi ItemsControl render xong danh sách.
            Dispatcher.UIThread.Post(UpdateEditControlsVisibility, DispatcherPriority.Loaded);
        }

        private void OnCreateClick(object? sender, RoutedEventArgs e)
        {
            var name = NewNameBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;

            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            var maxOrder = db.CustomLibraries.Any() ? db.CustomLibraries.Max(x => x.SortOrder) : -1;
            db.CustomLibraries.Add(new CustomLibrary { Name = name, SortOrder = maxOrder + 1 });
            db.SaveChanges();

            NewNameBox.Text = "";
            LoadLibraries();
        }

        private void OnLibraryClick(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Control c && c.Tag is CustomLibrarySummary lib)
                AppNavigator.NavigateTo(new CustomLibraryDetailPage(lib.Id, lib.Name));
        }

        // ----- Chế độ Sửa -----

        private void OnEditModeClick(object? sender, RoutedEventArgs e)
        {
            _isEditMode = !_isEditMode;
            EditModeButton.Content = _isEditMode ? "Xong" : "Sửa";
            UpdateEditControlsVisibility();
        }

        private void UpdateEditControlsVisibility()
        {
            foreach (var el in FindVisualChildren<Control>(LibrariesList))
            {
                if (el.Name == "DragHandleIcon" || el.Name == "LibraryEditControls")
                    el.IsVisible = _isEditMode;
            }
        }

        // ----- Kéo-thả đổi thứ tự -----
        // LƯU Ý: Avalonia DragDrop chạy bất đồng bộ (async), khác WPF DoDragDrop chạy đồng bộ.
        // Nên test kỹ phần này trên cả Desktop lẫn Android (Android hỗ trợ kéo-thả hạn chế hơn).

        private void OnDragHandlePointerPressed(object? sender, PointerPressedEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private async void OnDragHandlePointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_isEditMode || !e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
            if (sender is not Control c || c.DataContext is not CustomLibrarySummary lib) return;

            var pos = e.GetPosition(null);
            var diff = _dragStartPoint - pos;

            const double minDragDistance = 4; // Avalonia không có SystemParameters.MinimumHorizontalDragDistance sẵn
            if (Math.Abs(diff.X) > minDragDistance || Math.Abs(diff.Y) > minDragDistance)
            {
                _draggedItem = lib;
                var data = new DataObject();
                data.Set("library", lib);
                await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
            }
        }

        private void OnLibraryItemDrop(object? sender, DragEventArgs e)
        {
            if (_draggedItem == null) return;
            if (sender is not Control c || c.DataContext is not CustomLibrarySummary target)
            {
                _draggedItem = null;
                return;
            }
            if (target.Id == _draggedItem.Id)
            {
                _draggedItem = null;
                return;
            }

            if (LibrariesList.ItemsSource is not List<CustomLibrarySummary> items)
            {
                _draggedItem = null;
                return;
            }

            var oldIndex = items.FindIndex(x => x.Id == _draggedItem.Id);
            var newIndex = items.FindIndex(x => x.Id == target.Id);
            if (oldIndex < 0 || newIndex < 0)
            {
                _draggedItem = null;
                return;
            }

            var moved = _draggedItem;
            items.RemoveAt(oldIndex);
            items.Insert(newIndex, moved);

            using (var db = new MiaoDbContext(AppPaths.DbFilePath))
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var entity = db.CustomLibraries.Find(items[i].Id);
                    if (entity != null) entity.SortOrder = i;
                }
                db.SaveChanges();
            }

            _draggedItem = null;
            LoadLibraries();
        }

        // ----- Đổi tên -----

        private void OnRenameClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Control c || c.Tag is not CustomLibrarySummary lib) return;

            _renamingLibrary = lib;
            RenameBox.Text = lib.Name;
            ShowModal(RenameCard);
        }

        private void OnRenameSaveClick(object? sender, RoutedEventArgs e)
        {
            var newName = RenameBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(newName) || _renamingLibrary == null || newName == _renamingLibrary.Name)
            {
                ModalService.Close();
                return;
            }

            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            var entity = db.CustomLibraries.Find(_renamingLibrary.Id);
            if (entity != null)
            {
                entity.Name = newName;
                db.SaveChanges();
            }

            _renamingLibrary = null;
            ModalService.Close();
            LoadLibraries();
        }

        private void OnRenameCancelClick(object? sender, RoutedEventArgs e)
        {
            _renamingLibrary = null;
            ModalService.Close();
        }

        private void OnDeleteClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Control c || c.Tag is not CustomLibrarySummary lib) return;

            ShowConfirm($"Xóa bộ sưu tập \"{lib.Name}\"? Các truyện bên trong sẽ không bị xóa khỏi thư viện chung.", () =>
            {
                using var db = new MiaoDbContext(AppPaths.DbFilePath);
                var links = db.CustomLibraryNovels.Where(x => x.CustomLibraryId == lib.Id);
                db.CustomLibraryNovels.RemoveRange(links);

                var entity = db.CustomLibraries.Find(lib.Id);
                if (entity != null) db.CustomLibraries.Remove(entity);

                db.SaveChanges();
                LoadLibraries();
            });
        }
    }
}