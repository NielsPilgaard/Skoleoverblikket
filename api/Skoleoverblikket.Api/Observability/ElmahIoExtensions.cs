using Elmah.Io.AspNetCore;
using Elmah.Io.Extensions.Logging;

namespace Skoleoverblikket.Api.Observability;

public static class ElmahIoStartupExtensions
{
	public static WebApplicationBuilder AddElmahIoErrorLogging(this WebApplicationBuilder builder)
	{
		if (!TryGetOptions(builder.Configuration, out var apiKey, out var logId))
		{
			return builder;
		}

		builder.Services.AddElmahIo(options =>
		{
			options.ApiKey = apiKey;
			options.LogId = logId;
			// OnFilter returning true discards the message. Keep only Error and Fatal; drop everything less severe.
			options.OnFilter = (error) => error.Severity != "Error" && error.Severity != "Fatal";
		});

		builder.Logging.AddElmahIo(options =>
		{
			options.ApiKey = apiKey;
			options.LogId = logId;
		});
		builder.Logging.AddFilter<ElmahIoLoggerProvider>(null, LogLevel.Error);

		return builder;
	}

	public static WebApplication UseElmahIoErrorLogging(this WebApplication app)
	{
		if (TryGetOptions(app.Configuration, out _, out _))
		{
			app.UseElmahIo();
		}

		return app;
	}

	private static bool TryGetOptions(IConfiguration configuration, out string apiKey, out Guid logId)
	{
		apiKey = configuration["ElmahIo:ApiKey"] ?? string.Empty;
		var rawLogId = configuration["ElmahIo:LogId"];

		if (string.IsNullOrWhiteSpace(apiKey) || !Guid.TryParse(rawLogId, out logId))
		{
			logId = Guid.Empty;
			return false;
		}

		return true;
	}
}
