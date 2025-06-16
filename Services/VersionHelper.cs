using System.Reflection;

namespace AnkiPlus_MAUI.Services;

public static class VersionHelper
{
    /// <summary>
    /// 現在のアプリケーションバージョンを取得します
    /// </summary>
    public static string GetCurrentVersion()
    {
        try
        {
#if WINDOWS
            // Windows MSIX パッケージのバージョンを取得
            var package = Windows.ApplicationModel.Package.Current;
            var version = package.Id.Version;
            return $"{version.Major}.{version.Minor}.{version.Build}";
#else
            // 他のプラットフォーム用のフォールバック
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            return version?.ToString(3) ?? "1.0.0";
#endif
        }
        catch
        {
            // エラーが発生した場合のデフォルトバージョン
            return "1.0.0";
        }
    }

    /// <summary>
    /// アプリケーション情報を取得します
    /// </summary>
    public static AppInfo GetAppInfo()
    {
        return new AppInfo
        {
            Name = AppInfo.Current.Name,
            Version = GetCurrentVersion(),
            PackageName = AppInfo.Current.PackageName,
            BuildString = AppInfo.Current.BuildString
        };
    }

    /// <summary>
    /// バージョン文字列を比較します
    /// </summary>
    public static bool IsNewerVersion(string currentVersion, string newVersion)
    {
        try
        {
            var current = Version.Parse(currentVersion);
            var newer = Version.Parse(newVersion);
            return newer > current;
        }
        catch
        {
            return false;
        }
    }
}

public class AppInfo
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public string BuildString { get; set; } = string.Empty;
} 