using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Miao.Core.Data;
using Miao.Core.Services;
using Miao.Core.Models;
using Miao.UI.Services;

namespace Miao.UI.Views.Pages
{
    public partial class AddToLibraryView : UserControl
    {
        private readonly Guid _novelId;

        public AddToLibraryView(Guid novelId)
        {
            InitializeComponent();
            _novelId = novelId;
            LoadLibraries();
        }

        private void LoadLibraries()
        {
            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            LibrariesList.ItemsSource = db.CustomLibraries.ToList();
        }

        private void OnCreateNewClick(object? sender, RoutedEventArgs e)
        {
            var name = NewLibraryNameBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;

            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            var lib = new CustomLibrary { Name = name };
            db.CustomLibraries.Add(lib);
            db.SaveChanges();

            AddNovelToLibrary(lib.Id);
            ModalService.Close();
        }

        private void OnLibraryClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not CustomLibrary lib) return;
            AddNovelToLibrary(lib.Id);
            ModalService.Close();
        }

        private void AddNovelToLibrary(Guid libraryId)
        {
            using var db = new MiaoDbContext(AppPaths.DbFilePath);
            var exists = db.CustomLibraryNovels.Any(x => x.CustomLibraryId == libraryId && x.NovelId == _novelId);
            if (!exists)
            {
                db.CustomLibraryNovels.Add(new CustomLibraryNovel { CustomLibraryId = libraryId, NovelId = _novelId });
                db.SaveChanges();
            }
        }
    }
}