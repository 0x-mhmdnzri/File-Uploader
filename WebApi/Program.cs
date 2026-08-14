using WebApi.Events;
using WebApi.Interfaces;
using WebApi.Repositories;
using WebApi.Services;
using WebApi.Storages;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<IUploadRepository, InMemoryUploadRepository>();
builder.Services.AddSingleton<IFileStorage, FileSystemStorage>();
builder.Services.AddSingleton<IUploadEventBus, ChannelUploadEventBus>();
builder.Services.AddScoped<IUploadService, UploadService>();
builder.Services.AddHostedService<UploadCleanupService>();

builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("StorageOptions"));

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy
            .WithOrigins("https://localhost:7097")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();
app.UseCors("AllowFrontend");
app.MapControllers();

app.Run();
