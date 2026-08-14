using Microsoft.EntityFrameworkCore;
using Serilog;
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

// ---------- Serilog bootstrap ----------
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
            shared: true));

    // ---------- Services ----------
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    builder.Services.Configure<StorageOptions>(
        builder.Configuration.GetSection(StorageOptions.SectionName));
    builder.Services.Configure<WebhookOptions>(
        builder.Configuration.GetSection(WebhookOptions.SectionName));

    var connectionString = builder.Configuration.GetConnectionString("Default")
                           ?? "Data Source=uploads.db";

    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseSqlite(connectionString));

    builder.Services.AddScoped<IUploadRepository, EfUploadRepository>();
    builder.Services.AddSingleton<IFileHasher, Sha256FileHasher>();
    builder.Services.AddSingleton<IFileStorage, FileSystemStorage>();
    builder.Services.AddScoped<IUploadService, UploadService>();
    builder.Services.AddSingleton<IUploadMetrics, UploadMetrics>();

    // ---- In-process event bus ----
    builder.Services.AddSingleton<ChannelUploadEventBus>();
    builder.Services.AddSingleton<IUploadEventPublisher, ChannelUploadEventPublisher>();
    builder.Services.AddHostedService<UploadEventDispatcherService>();

    // Handlers (add more without touching UploadService)
    builder.Services.AddSingleton<IUploadEventHandler, LoggingUploadEventHandler>();
    builder.Services.AddHttpClient<WebhookUploadEventHandler>();
    builder.Services.AddSingleton<IUploadEventHandler>(sp =>
        sp.GetRequiredService<WebhookUploadEventHandler>());

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
                    "https://localhost:7097",
                    "http://localhost:5097",
                    "http://localhost:3000")
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
    app.MapControllers();

    app.MapHealthChecks("/health");
    app.MapGet("/api/metrics", (IUploadMetrics metrics) => Results.Ok(metrics.Snapshot()));

    Log.Information("File Uploader API starting");
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
