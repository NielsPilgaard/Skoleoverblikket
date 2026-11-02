using Elmah.Io.AspNetCore;
using Elmah.Io.Client;
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

		// Set on every message, so errors during the container switchover are attributed to the
		// version that threw them rather than to the latest elmah.io deployment.
		var version = builder.Configuration[$"{ApplicationOptions.SectionName}:{nameof(ApplicationOptions.Version)}"];
		void OnMessage(CreateMessage message)
		{
			message.Version = version;
			ElmahIoScrubber.Scrub(message);
		}

		builder.Services.AddElmahIo(options =>
		{
			options.ApiKey = apiKey;
			options.LogId = logId;
			// OnFilter returning true discards the message. Keep only Error and Fatal; drop everything less severe.
			options.OnFilter = (error) => error.Severity != "Error" && error.Severity != "Fatal";
			options.OnMessage = OnMessage;
		});

		builder.Logging.AddElmahIo(options =>
		{
			options.ApiKey = apiKey;
			options.LogId = logId;
			options.OnMessage = OnMessage;
		});
		// ExceptionHandlerMiddleware logs every unhandled exception at Error level, and
		// UseElmahIo() already reports those same exceptions. Drop this category from the
		// elmah.io logger provider so we don't get a duplicate entry per unhandled exception.
		builder.Logging.AddFilter<ElmahIoLoggerProvider>(
			"Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", LogLevel.None);
		// An unhealthy /api/health is logged at Error on every poll, and Keycloak answers 404
		// for the realm for a few seconds after every deploy. The CD watch job and the uptime
		// monitor already alert on /api/health, so these would only be duplicate emails.
		builder.Logging.AddFilter<ElmahIoLoggerProvider>(
			"Microsoft.Extensions.Diagnostics.HealthChecks.DefaultHealthCheckService", LogLevel.None);
		builder.Logging.AddFilter<ElmahIoLoggerProvider>(null, LogLevel.Error);

		return builder;
	}

	public static WebApplication UseElmahIoErrorLogging(this WebApplication app)
	{
		if (TryGetOptions(app.Configuration, out _, out _))
		{
			app.UseElmahIo();
			// A client that closes the request (page reload, navigating away) cancels
			// RequestAborted, and the OperationCanceledException would reach UseElmahIo as an
			// error. Answer 499 here instead, as ExceptionHandlerMiddleware does, so elmah.io
			// never sees it. Server-side timeouts don't cancel RequestAborted and still get logged.
			app.Use(async (context, next) =>
			{
				try
				{
					await next(context);
				}
				catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
				{
					if (!context.Response.HasStarted)
					{
						context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
					}
				}
			});
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
