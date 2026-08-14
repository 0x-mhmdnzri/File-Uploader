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
    options.AddDefaultPolicy(policy =>
    {
        policy
            .SetIsOriginAllowed(origin =>
            {
                // Allow any localhost / 127.0.0.1 with any port in development
                if (string.IsNullOrEmpty(origin)) return false;
                try
                {
                    var uri = new Uri(origin);
                    return uri.Host is "localhost" or "127.0.0.1";
                }
                catch { return false; }
            })
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
app.UseCors();
app.MapControllers();

app.Run();
