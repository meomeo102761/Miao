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
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public int Count { get; set; }
    }

    public partial class CustomLibrariesPage : ConfirmablePage
    {
        private bool _isEditMode;

        protected override Control ConfirmCardElement => ConfirmCard;
        protected override TextBlock ConfirmMessageTextElement => ConfirmMessageText;

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

        private void OnLibraryItemDrop(object? sender, DragEventArgs e)
        {
            if (_draggedLibrary == null) return;
            if (sender is not StyledElement fe || fe.DataContext is not CustomLibrarySummary target)
            {
                _draggedLibrary = null;
                return;
            }

            if (target.Id == _draggedLibrary.Id)
            {
                _draggedLibrary = null;
                return;
            }

            if (LibrariesList.ItemsSource is not List<CustomLibrarySummary> items)
            {
                _draggedLibrary = null;
                return;
            }

            var oldIndex = items.FindIndex(x => x.Id == _draggedLibrary.Id);
            var newIndex = items.FindIndex(x => x.Id == target.Id);
            if (oldIndex < 0 || newIndex < 0)
            {
                _draggedLibrary = null;
                return;
            }

            var moved = _draggedLibrary;
            items.RemoveAt(oldIndex);
            items.Insert(newIndex, moved);

            _draggedLibrary = null;
            SaveOrderAndReload(items);
        }

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

        private const double DragThreshold = 5.0;
        private Point _dragStartPoint;
        private CustomLibrarySummary? _draggedLibrary;
        private PointerPressedEventArgs? _dragPressedEvent;

        private void OnDragHandleMouseDown(object? sender, PointerPressedEventArgs e)
        {
            _dragStartPoint = e.GetPosition(this);
            _dragPressedEvent = e;
        }

        private async void OnDragHandleMouseMove(object? sender, PointerEventArgs e)
        {
            if (!_isEditMode ||
                _dragPressedEvent == null ||
                !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;

            if (sender is not Control control || control.DataContext is not CustomLibrarySummary lib)
                return;

            var pos = e.GetPosition(this);
            var diff = _dragStartPoint - pos;

            if (Math.Abs(diff.X) <= DragThreshold && Math.Abs(diff.Y) <= DragThreshold)
                return;

            _draggedLibrary = lib;

            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText(lib.Id.ToString()));

            var pressedEvent = _dragPressedEvent;
            _dragPressedEvent = null;

            await DragDrop.DoDragDropAsync(pressedEvent, data, DragDropEffects.Move);
        }

        private void SaveOrderAndReload(List<CustomLibrarySummary> items)
        {
            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            for (int i = 0; i < items.Count; i++)
            {
                var entity = db.CustomLibraries.Find(items[i].Id);
                if (entity != null) entity.SortOrder = i;
            }
            db.SaveChanges();
            LoadLibraries();
        }        

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