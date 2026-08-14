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

    builder.Host.UseSerilog((context, services, configuration) =&gt; configuration
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

    builder.Services.Configure&lt;StorageOptions&gt;(
        builder.Configuration.GetSection(StorageOptions.SectionName));
    builder.Services.Configure&lt;WebhookOptions&gt;(
        builder.Configuration.GetSection(WebhookOptions.SectionName));

    var connectionString = builder.Configuration.GetConnectionString("Default")
                           ?? "Data Source=uploads.db";

    builder.Services.AddDbContext&lt;AppDbContext&gt;(options =&gt;
        options.UseSqlite(connectionString));

    builder.Services.AddScoped&lt;IUploadRepository, EfUploadRepository&gt;();
    builder.Services.AddSingleton&lt;IFileHasher, Sha256FileHasher&gt;();
    builder.Services.AddSingleton&lt;IFileStorage, FileSystemStorage&gt;();
    builder.Services.AddSingleton&lt;IReceivedChunkCache, ReceivedChunkCache&gt;();
    builder.Services.AddScoped&lt;IUploadService, UploadService&gt;();
    builder.Services.AddSingleton&lt;IUploadMetrics, UploadMetrics&gt;();

    // ---- In-process event bus ----
    builder.Services.AddSingleton&lt;ChannelUploadEventBus&gt;();
    builder.Services.AddSingleton&lt;IUploadEventPublisher, ChannelUploadEventPublisher&gt;();
    builder.Services.AddHostedService&lt;UploadEventDispatcherService&gt;();

    // Handlers (add more without touching UploadService)
    builder.Services.AddSingleton&lt;IUploadEventHandler, LoggingUploadEventHandler&gt;();
    builder.Services.AddHttpClient&lt;WebhookUploadEventHandler&gt;();
    builder.Services.AddSingleton&lt;IUploadEventHandler&gt;(sp =&gt;
        sp.GetRequiredService&lt;WebhookUploadEventHandler&gt;());

    builder.Services.AddHostedService&lt;OrphanCleanupService&gt;();

    builder.Services.AddHealthChecks()
        .AddDbContextCheck&lt;AppDbContext&gt;("database")
        .AddCheck&lt;StorageHealthCheck&gt;("storage");

    builder.Services.AddCors(options =&gt;
    {
        options.AddPolicy("AllowFrontend", policy =&gt;
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
        var db = scope.ServiceProvider.GetRequiredService&lt;AppDbContext&gt;();
        await db.Database.EnsureCreatedAsync();
    }

    app.UseSerilogRequestLogging(opts =&gt;
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
    app.MapGet("/api/metrics", (IUploadMetrics metrics) =&gt; Results.Ok(metrics.Snapshot()));

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
