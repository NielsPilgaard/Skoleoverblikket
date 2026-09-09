using Microsoft.AspNetCore.HttpLogging;
using Skoleoverblikket.Api;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Cache;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Email;
using Skoleoverblikket.Api.Observability;
using Skoleoverblikket.Api.OpenApi;
using Skoleoverblikket.Api.Services;
using Skoleoverblikket.Api.Storage;
using Skoleoverblikket.Api.Tenancy;
using Skoleoverblikket.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddHttpLogging(options =>
{
	options.LoggingFields = HttpLoggingFields.RequestMethod
		| HttpLoggingFields.RequestPath
		| HttpLoggingFields.RequestQuery
		| HttpLoggingFields.ResponseStatusCode
		| HttpLoggingFields.Duration;
});

builder.Services.AddTenancy();
builder.Services.AddFusionCacheDefaults();
builder.Services.AddDatabase(builder.Configuration);
builder.Services.AddKeycloakAuth();
builder.Services.AddKeycloakAdmin();
builder.Services.AddOpenApi();
builder.Services.AddEmail();
builder.Services.AddObjectStorage();
builder.Services.AddStripe();
builder.Services.AddDomainServices();
builder.Services.AddApiRateLimiting();

builder.AddElmahIoErrorLogging();

var app = builder.Build();

app.UseSwaggerInDevelopment();
app.UseHttpLogging();
app.UseExceptionHandler();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.UseElmahIoErrorLogging();

app.MapControllers();
app.MapDefaultEndpoints();

await app.MigrateAndSeedAsync();

app.Run();
