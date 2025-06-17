using Microsoft.Extensions.Logging;
using Plugin.Maui.KeyListener;
using SkiaSharp.Views.Maui.Controls.Hosting;
using AnkiPlus_MAUI.Services;
using AnkiPlus_MAUI.ViewModels;

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
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansRegular");
			});

		// サービスの登録
		builder.Services.AddSingleton<ConfigurationService>();
		builder.Services.AddSingleton<CardSyncService>();
		builder.Services.AddSingleton<BlobStorageService>();
		builder.Services.AddSingleton<AnkiExporter>();
		builder.Services.AddSingleton<AnkiImporter>();
		
		// HTTP Client の登録
		builder.Services.AddHttpClient<GitHubUpdateService>();
		
		// アップデート関連サービス
		builder.Services.AddSingleton<GitHubUpdateService>();
		builder.Services.AddSingleton<UpdateNotificationService>();

		// ViewModels の登録
		builder.Services.AddTransient<MainPageViewModel>();

		// Pages の登録
		builder.Services.AddTransient<MainPage>();

#if DEBUG
		builder.Services.AddLogging(logging =>
		{
			logging.AddDebug();
		});
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
