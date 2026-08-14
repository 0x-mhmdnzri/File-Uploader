using Microsoft.EntityFrameworkCore;
using Serilog;
using WebApi.Audit;
using WebApi.Auth;
using WebApi.BackgroundServices;
using WebApi.Data;
using WebApi.Events;
using WebApi.Events.Handlers;
using WebApi.Hashing;
using WebApi.Health;
using WebApi.Interfaces;
using WebApi.Metrics;
using WebApi.Repositories;
using WebApi.Services;
using WebApi.Storages;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "FileUploader.WebApi")
        .WriteTo.Console()
        .WriteTo.File(
            path: "logs/uploader-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14,
            shared: true)
        .WriteTo.File(
            path: "logs/audit-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            shared: true,
            restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information));

    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    builder.Services.Configure<StorageOptions>(
        builder.Configuration.GetSection(StorageOptions.SectionName));
    builder.Services.Configure<ObjectStorageOptions>(
        builder.Configuration.GetSection(ObjectStorageOptions.SectionName));
    builder.Services.Configure<WebhookOptions>(
        builder.Configuration.GetSection(WebhookOptions.SectionName));
    builder.Services.Configure<AuthOptions>(
        builder.Configuration.GetSection(AuthOptions.SectionName));
    builder.Services.Configure<RabbitMqOptions>(
        builder.Configuration.GetSection(RabbitMqOptions.SectionName));

    var connectionString = builder.Configuration.GetConnectionString("Default")
                           ?? "Data Source=uploads.db";

    // D1: Sqlite (single node / lab) or Postgres (shared metadata across API instances).
    var dbProvider = builder.Configuration["Database:Provider"] ?? "Sqlite";
    builder.Services.AddDbContext<AppDbContext>(options =>
    {
        if (string.Equals(dbProvider, "Postgres", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(dbProvider, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            options.UseNpgsql(connectionString);
        }
        else
        {
            options.UseSqlite(connectionString);
        }
    });

    builder.Services.AddScoped<IUploadRepository, EfUploadRepository>();

    var hasherMode = builder.Configuration.GetSection(StorageOptions.SectionName)["Hasher"] ?? "Hardware";
    if (string.Equals(hasherMode, "Cpu", StringComparison.OrdinalIgnoreCase))
        builder.Services.AddSingleton<IFileHasher, Sha256FileHasher>();
    else
        builder.Services.AddSingleton<IFileHasher, HardwareSha256FileHasher>();

    var provider = builder.Configuration.GetSection(StorageOptions.SectionName)["Provider"] ?? "FileSystem";
    if (string.Equals(provider, "S3", StringComparison.OrdinalIgnoreCase))
        builder.Services.AddSingleton<IFileStorage, S3FileStorage>();
    else
        builder.Services.AddSingleton<IFileStorage, FileSystemStorage>();

    builder.Services.AddSingleton<IReceivedChunkCache, ReceivedChunkCache>();
    builder.Services.AddSingleton<ISessionCache>(sp =>
    {
        var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<StorageOptions>>().Value;
        var ttl = TimeSpan.FromSeconds(Math.Max(5, opts.SessionCacheTtlSeconds));
        return new SessionCache(ttl);
    });
    builder.Services.AddSingleton<IAuditLogger, SerilogAuditLogger>();
    builder.Services.AddScoped<IUploadService, UploadService>();
    builder.Services.AddSingleton<IUploadMetrics, UploadMetrics>();

    builder.Services.AddSingleton<ChannelUploadEventBus>();
    builder.Services.AddSingleton<IUploadEventPublisher, ChannelUploadEventPublisher>();
    builder.Services.AddHostedService<UploadEventDispatcherService>();

    builder.Services.AddSingleton<IUploadEventHandler, LoggingUploadEventHandler>();
    builder.Services.AddHttpClient<WebhookUploadEventHandler>();
    builder.Services.AddSingleton<IUploadEventHandler>(sp =>
        sp.GetRequiredService<WebhookUploadEventHandler>());

    builder.Services.AddSingleton<RabbitMqUploadEventHandler>();
    builder.Services.AddSingleton<IUploadEventHandler>(sp =>
        sp.GetRequiredService<RabbitMqUploadEventHandler>());

    builder.Services.AddHostedService<OrphanCleanupService>();

    builder.Services.AddHealthChecks()
        .AddDbContextCheck<AppDbContext>("database")
        .AddCheck<StorageHealthCheck>("storage");

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowFrontend", policy =>
        {
            policy
                .WithOrigins(
                    "http://localhost:5074",
                    "https://localhost:5074",
                    "http://localhost:5073",
                    "https://localhost:5073",
                    "https://localhost:7097",
                    "http://localhost:5097",
                    "http://localhost:3000",
                    "http://localhost:5173",
                    "https://localhost:5173")
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
    });

    var app = builder.Build();

    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    app.UseSerilogRequestLogging(opts =>
    {
        opts.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    });

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseRouting();
    app.UseCors("AllowFrontend");
    app.UseMiddleware<ApiKeyMiddleware>();
    app.MapControllers();

    app.MapHealthChecks("/health");
    app.MapGet("/api/metrics", (IUploadMetrics metrics) => Results.Ok(metrics.Snapshot()));

    Log.Information(
        "File Uploader API starting Db={DbProvider} Storage={Provider} Hasher={Hasher}",
        dbProvider, provider, hasherMode);
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
