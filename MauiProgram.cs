using Microsoft.Extensions.Logging;
using Plugin.Maui.KeyListener;
using SkiaSharp.Views.Maui.Controls.Hosting;
using AnkiPlus_MAUI.Services;

namespace AnkiPlus_MAUI;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
            .UseSkiaSharp()
            .UseKeyListener()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		builder.Services.AddSingleton<BlobStorageService>();
		builder.Services.AddSingleton<CardSyncService>();

		// HTTPクライアントとGitHub Update Serviceを追加
		builder.Services.AddHttpClient();
		builder.Services.AddSingleton<GitHubUpdateService>();
		builder.Services.AddSingleton<UpdateNotificationService>();
		
		// MainPageとLoginPageをDIに登録
		builder.Services.AddTransient<MainPage>();
		builder.Services.AddTransient<LoginPage>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
