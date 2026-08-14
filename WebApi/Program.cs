using Microsoft.EntityFrameworkCore;
using WebApi.BackgroundServices;
using WebApi.Data;
using WebApi.Interfaces;
using WebApi.Repositories;
using WebApi.Services;
using WebApi.Storages;

var builder = WebApplication.CreateBuilder(args);

// ---------- Services ----------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Storage options
builder.Services.Configure<StorageOptions>(
    builder.Configuration.GetSection(StorageOptions.SectionName));

// EF Core + SQLite
var connectionString = builder.Configuration.GetConnectionString("Default")
                       ?? "Data Source=uploads.db";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString));

// Repositories & services
builder.Services.AddScoped<IUploadRepository, EfUploadRepository>();
builder.Services.AddSingleton<IFileStorage, FileSystemStorage>();
builder.Services.AddScoped<IUploadService, UploadService>();

// Background cleanup of orphan (expired pending) uploads
builder.Services.AddHostedService<OrphanCleanupService>();

// CORS
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

// Ensure database is created / migrated on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();
app.UseCors("AllowFrontend");
app.MapControllers();

app.Run();
