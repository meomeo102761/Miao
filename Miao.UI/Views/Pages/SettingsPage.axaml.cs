using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Microsoft.Data.Sqlite;
using Miao.Core.Services;
using Miao.UI.Services;

namespace Miao.UI.Views.Pages
{
    public partial class SettingsPage : UserControl
    {
        private static readonly SolidColorBrush ErrorBrush = new(Color.Parse("#B94A48"));

        public SettingsPage()
        {
            InitializeComponent();
            FolderTextBox.Text = AppSettingsService.Instance.Settings.DataFolder;

            var engine = AppSettingsService.Instance.Settings.TranslationEngine;
            TranslationEngineBox.SelectedValue =
                string.Equals(engine, "Dictionary", StringComparison.OrdinalIgnoreCase) ? "Dictionary" : "DichNgay";
        }

        private async void OnBrowse(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider is not { } storageProvider) return;

            var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Chọn thư mục lưu dữ liệu Miao",
                AllowMultiple = false
            });

            var folder = folders.FirstOrDefault();
            if (folder?.TryGetLocalPath() is { } localPath)
                FolderTextBox.Text = localPath;
        }

        private void SetStatus(string text, bool isError)
        {
            SavedText.Text = text;
            SavedText.Foreground = isError ? ErrorBrush : (IBrush)this.FindResource("AccentJade")!;
        }

        private void SetBusy(bool busy)
        {
            SaveButton.IsEnabled = !busy;
            BrowseButton.IsEnabled = !busy;
            TranslationEngineBox.IsEnabled = !busy;
        }

        private async void OnSave(object? sender, RoutedEventArgs e)
        {
            var settings = AppSettingsService.Instance.Settings;

            if (TranslationEngineBox.SelectedValue is string engine && (engine == "DichNgay" || engine == "Dictionary"))
                settings.TranslationEngine = engine;

            var newFolderRaw = FolderTextBox.Text?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(newFolderRaw))
            {
                SetStatus("Chưa chọn thư mục.", isError: true);
                return;
            }

            string oldFolder, newFolder;
            try
            {
                oldFolder = Path.GetFullPath(settings.DataFolder);
                newFolder = Path.GetFullPath(newFolderRaw);
            }
            catch (Exception ex)
            {
                SetStatus($"Đường dẫn không hợp lệ: {ex.Message}", isError: true);
                return;
            }

            if (string.Equals(oldFolder, newFolder, StringComparison.OrdinalIgnoreCase))
            {
                settings.DataFolder = newFolder;
                AppSettingsService.Instance.Save();
                SetStatus("Đã lưu cài đặt.", isError: false);
                return;
            }

            var oldFull = oldFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                    + Path.DirectorySeparatorChar;
            var newFull = newFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                    + Path.DirectorySeparatorChar;

            if (newFull.StartsWith(oldFull, StringComparison.OrdinalIgnoreCase) ||
                oldFull.StartsWith(newFull, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus(
                    "Không thể chọn thư mục mới nằm bên trong hoặc chứa thư mục dữ liệu hiện tại.",
                    isError: true);
                return;
            }

            var oldDbPath = Path.Combine(oldFolder, "miao.db");
            var newDbPath = Path.Combine(newFolder, "miao.db");

            if (!File.Exists(oldDbPath))
            {
                try
                {
                    Directory.CreateDirectory(newFolder);

                    settings.DataFolder = newFolder;
                    AppSettingsService.Instance.Save();

                    FolderTextBox.Text = newFolder;
                    SetStatus("Đã lưu cài đặt.", isError: false);
                }
                catch (Exception ex)
                {
                    SetStatus($"Lỗi: {ex.Message}", isError: true);
                }

                return;
            }

            if (File.Exists(newDbPath))
            {
                var overwrite = await DialogService.ShowYesNoAsync(
                    "Thư mục mới đã có sẵn dữ liệu Miao khác.\n\n" +
                    "Ghi đè dữ liệu hiện tại lên dữ liệu ở thư mục mới?",
                    "Xác nhận");

                if (overwrite != DialogResult.Yes)
                    return;
            }

            var confirm = await DialogService.ShowYesNoAsync(
                $"Toàn bộ dữ liệu sẽ chuyển sang:\n\n{newFolder}\n\n" +
                "Sau khi chuyển thành công, thư mục cũ sẽ bị XOÁ HOÀN TOÀN.\n" +
                "Thao tác này không thể hoàn tác.\n\n" +
                "Tiếp tục?",
                "Xác nhận chuyển dữ liệu");

            if (confirm != DialogResult.Yes)
                return;

            SetBusy(true);
            try
            {
                SetStatus("Đang đóng kết nối dữ liệu...", isError: false);

                SqliteConnection.ClearAllPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();

                Directory.CreateDirectory(newFolder);

                SetStatus("Đang sao chép dữ liệu...", isError: false);
                await Task.Run(() => CopyDirectory(oldFolder, newFolder, overwrite: true));

                SqliteConnection.ClearAllPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();

                SetStatus("Đang xoá dữ liệu cũ...", isError: false);

                try
                {
                    await Task.Run(() => Directory.Delete(oldFolder, recursive: true));
                }
                catch (IOException)
                {
                    settings.DataFolder = newFolder;
                    AppSettingsService.Instance.Save();
                    FolderTextBox.Text = newFolder;
                    SetStatus(
                        $"Đã chuyển dữ liệu, nhưng chưa xoá được thư mục cũ:\n{oldFolder}\n" +
                        "Bạn có thể xoá thủ công thư mục này sau.",
                        isError: true);
                    return;
                }

                settings.DataFolder = newFolder;
                AppSettingsService.Instance.Save();

                FolderTextBox.Text = newFolder;
                SetStatus("Đã chuyển dữ liệu và lưu cài đặt.", isError: false);
            }
            catch (IOException ex)
            {
                SetStatus(
                    $"Không thể chuyển dữ liệu:\n{ex.Message}\n\n" +
                    "Có file dữ liệu vẫn đang được sử dụng. " +
                    "Hãy đóng các trang đang thao tác với dữ liệu rồi thử lại.",
                    isError: true);
            }
            catch (Exception ex)
            {
                SetStatus($"Lỗi khi chuyển dữ liệu:\n{ex.Message}", isError: true);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void CopyDirectory(string sourceDir, string destDir, bool overwrite)
        {
            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, overwrite);
            }

            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                var destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
                CopyDirectory(subDir, destSubDir, overwrite);
            }
        }
    }
}