using Microsoft.EntityFrameworkCore;
using RFIDPoker.Api.Data;
using RFIDPoker.Api.Hubs;
using RFIDPoker.Api.Models;
using RFIDPoker.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Poker analysis services
builder.Services.AddSingleton<ITableStateManager, TableStateManager>();
builder.Services.AddSingleton<IHandEvaluator, HandEvaluator>();
builder.Services.AddSingleton<IEquityCalculator, EquityCalculator>();
builder.Services.AddSingleton<PokerAnalysisEngine>();
builder.Services.AddSingleton<IPokerAnalysisEngine>(sp => sp.GetRequiredService<PokerAnalysisEngine>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<PokerAnalysisEngine>());

// RFID reader services
builder.Services.Configure<RfidConfig>(builder.Configuration.GetSection(RfidConfig.SectionName));

var connectionString = builder.Configuration.GetConnectionString("AppDb")
    ?? "Data Source=rfidpoker.db";
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString));

builder.Services.AddSingleton<ICardTagMapper, CardTagMapper>();
builder.Services.AddSingleton<RfidReaderService>();
builder.Services.AddSingleton<IRfidReaderService>(sp => sp.GetRequiredService<RfidReaderService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<RfidReaderService>());
builder.Services.AddHostedService<IdleHandResetService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseCors();

app.UseAuthorization();

app.MapControllers();
app.MapHub<AnalysisHub>("/hubs/analysis");

app.Run();
