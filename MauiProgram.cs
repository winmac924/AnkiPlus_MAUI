using Microsoft.Extensions.Logging;
using Plugin.Maui.KeyListener;
using SkiaSharp.Views.Maui.Controls.Hosting;


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
#if DEBUG
        builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
