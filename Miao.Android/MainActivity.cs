using System;
using System.IO;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;
using Android.Provider;
using Avalonia;
using Avalonia.Android;
using Miao.Android.Services;
using Miao.Core.Services;
using Miao.UI.Services;

namespace Miao.Android;

[Activity(
    Label = "Miao.Android",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        var hasPublicAccess = HasAllFilesAccess();

        // baseFolder: AppSettingsService tự nối thêm "\Miao" phía sau, không tự thêm ở đây
        // kẻo bị lồng 2 lần thành ".../Miao/Miao".
        var baseFolder = hasPublicAccess
            ? global::Android.OS.Environment.ExternalStorageDirectory!.AbsolutePath
            : FilesDir!.AbsolutePath;

        AppSettingsService.Initialize(baseFolder);

        if (hasPublicAccess)
            MigrateFromPrivateFolderIfNeeded();
        else
            RequestAllFilesAccess();

        var fetcher = new NotSupportedPageFetcher();
        PlatformServices.PageFetcher = fetcher;
        PlatformServices.ScreenshotFetcher = fetcher;

        base.OnCreate(savedInstanceState);
    }

    private bool HasAllFilesAccess()
    {
        // OperatingSystem.IsAndroidVersionAtLeast(30) là kiểu kiểm tra mà chính trình phân tích
        // CA1416 của .NET nhận diện được là "lá chắn" an toàn cho API chỉ có từ Android 11 -> hết warning.
        if (!OperatingSystem.IsAndroidVersionAtLeast(30)) return true;
        return global::Android.OS.Environment.IsExternalStorageManager;
    }

    private void RequestAllFilesAccess()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(30)) return;

        try
        {
            var intent = new Intent(Settings.ActionManageAppAllFilesAccessPermission);
            intent.SetData(global::Android.Net.Uri.Parse($"package:{PackageName}"));
            StartActivity(intent);
        }
        catch
        {
            // Một số ROM Android tùy biến không hỗ trợ màn hình cài đặt riêng cho từng app
            // -> fallback sang màn hình danh sách chung để người dùng tự tìm Miao và cấp quyền.
            StartActivity(new Intent(Settings.ActionManageAllFilesAccessPermission));
        }
    }

    // Nếu trước đây app từng chạy chưa có quyền (lưu dữ liệu ở thư mục riêng FilesDir/Miao),
    // sau khi người dùng cấp quyền và mở lại app -> tự chuyển dữ liệu cũ sang thư mục công khai
    // để không bị mất truyện/chương đã tải trước đó.
    private void MigrateFromPrivateFolderIfNeeded()
    {
        try
        {
            var oldFolder = Path.Combine(FilesDir!.AbsolutePath, "Miao");
            var oldDbPath = Path.Combine(oldFolder, "miao.db");
            var newDbPath = Path.Combine(AppSettingsService.Instance.Settings.DataFolder, "miao.db");

            if (!File.Exists(oldDbPath) || File.Exists(newDbPath)) return;

            CopyDirectory(oldFolder, AppSettingsService.Instance.Settings.DataFolder);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MigrateFromPrivateFolderIfNeeded] Lỗi chuyển dữ liệu cũ: {ex}");
        }
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: false);

        foreach (var subDir in Directory.GetDirectories(sourceDir))
            CopyDirectory(subDir, Path.Combine(destDir, Path.GetFileName(subDir)));
    }
}