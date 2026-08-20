using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RFIDPoker.Api.Auth;
using RFIDPoker.Api.Data;
using RFIDPoker.Api.Hubs;
using RFIDPoker.Api.Models;
using RFIDPoker.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ---- Data directory + SQLite -----------------------------------------------
// Customer runtime data lives OUTSIDE the app binaries. Default matches the
// deployment layout:
//   PokerApp/
//     App/        <-- ContentRoot
//     Data/       <-- {ContentRoot}/../Data
var configuredDataDir = builder.Configuration["DataDirectory"];
var dataDirectory = string.IsNullOrWhiteSpace(configuredDataDir)
	? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "Data"))
	: Path.GetFullPath(configuredDataDir);
Directory.CreateDirectory(dataDirectory);

var dbFileName = builder.Environment.IsDevelopment() ? "development.db" : "poker.db";
var dbPath = Path.Combine(dataDirectory, dbFileName);
var connectionString = builder.Configuration.GetConnectionString("AppDb")
	?? $"Data Source={dbPath}";

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString));

// ---- Identity ---------------------------------------------------------------
builder.Services.AddIdentityCore<ApplicationUser>(options =>
	{
		options.User.RequireUniqueEmail = false;
		options.Password.RequireDigit = false;
		options.Password.RequireLowercase = false;
		options.Password.RequireUppercase = false;
		options.Password.RequireNonAlphanumeric = false;
		options.Password.RequiredLength = 6;
	})
	.AddRoles<IdentityRole>()
	.AddEntityFrameworkStores<AppDbContext>()
	.AddSignInManager()
	.AddDefaultTokenProviders();

// ---- Authentication (JWT — dual scheme for user + overlay) -----------------
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtSecret = jwtSection["Secret"];
if (string.IsNullOrWhiteSpace(jwtSecret))
{
	// Persist a machine-local signing key on first run so restarts don't invalidate tokens.
	var keyPath = Path.Combine(dataDirectory, "jwt.key");
	if (File.Exists(keyPath))
	{
		jwtSecret = File.ReadAllText(keyPath);
	}
	else
	{
		var buf = new byte[64];
		System.Security.Cryptography.RandomNumberGenerator.Fill(buf);
		jwtSecret = Convert.ToBase64String(buf);
		File.WriteAllText(keyPath, jwtSecret);
	}
}
var jwtIssuer = jwtSection["Issuer"] ?? "RFIDPoker";
var jwtAudience = jwtSection["Audience"] ?? "RFIDPoker";
var jwtKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));

builder.Services.AddSingleton(new JwtOptions(jwtIssuer, jwtAudience, jwtKey));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
	.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
	{
		options.TokenValidationParameters = new TokenValidationParameters
		{
			ValidateIssuer = true,
			ValidateAudience = true,
			ValidateLifetime = true,
			ValidateIssuerSigningKey = true,
			ValidIssuer = jwtIssuer,
			ValidAudience = jwtAudience,
			IssuerSigningKey = jwtKey,
			ClockSkew = TimeSpan.FromMinutes(1)
		};
		// Support token in query string for SignalR (browsers/OBS can't set Authorization
		// headers on WebSocket handshakes and OBS can't set them at all).
		options.Events = new JwtBearerEvents
		{
			OnMessageReceived = ctx =>
			{
				var accessToken = ctx.Request.Query["access_token"].FirstOrDefault()
								  ?? ctx.Request.Query["token"].FirstOrDefault();
				var path = ctx.HttpContext.Request.Path;
				if (!string.IsNullOrEmpty(accessToken) &&
					(path.StartsWithSegments("/hubs") || path.StartsWithSegments("/api/overlay")))
				{
					ctx.Token = accessToken;
				}
				return Task.CompletedTask;
			},
			OnTokenValidated = async ctx =>
			{
				// Reject overlay JWTs whose backing DB row has been revoked or expired
				// (JWT expiry is checked already; this catches admin-side revocation).
				var tokenType = ctx.Principal?.FindFirst(AuthClaims.TokenType)?.Value;
				if (tokenType == AuthClaims.OverlayTokenType)
				{
					var idStr = ctx.Principal!.FindFirst("overlay_id")?.Value;
					if (!int.TryParse(idStr, out var id))
					{
						ctx.Fail("Invalid overlay token id");
						return;
					}
					var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
					var row = await db.OverlayTokens.AsNoTracking()
						.FirstOrDefaultAsync(t => t.Id == id, ctx.HttpContext.RequestAborted);
					if (row is null || row.IsRevoked || row.ExpiresAt <= DateTimeOffset.UtcNow)
						ctx.Fail("Overlay token revoked or expired");
				}
				else if (tokenType == AuthClaims.UserTokenType)
				{
					// Ensure user still exists and is active.
					var userId = ctx.Principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value;
					if (string.IsNullOrEmpty(userId)) { ctx.Fail("Invalid user token"); return; }
					var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
					var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, ctx.HttpContext.RequestAborted);
					if (u is null || !u.IsActive) ctx.Fail("User inactive");
				}
			}
		};
	});

builder.Services.AddAuthorization(options =>
{
	options.AddPolicy(AuthPolicies.RequireAdmin, p =>
		p.RequireAuthenticatedUser()
		 .RequireClaim(AuthClaims.TokenType, AuthClaims.UserTokenType)
		 .RequireRole(AuthRoles.Admin));

	options.AddPolicy(AuthPolicies.RequireUser, p =>
		p.RequireAuthenticatedUser()
		 .RequireClaim(AuthClaims.TokenType, AuthClaims.UserTokenType));

	options.AddPolicy(AuthPolicies.OverlayRead, p =>
		p.RequireAuthenticatedUser()
		 .RequireClaim(AuthClaims.TokenType, AuthClaims.OverlayTokenType));

	options.AddPolicy(AuthPolicies.UserOrOverlay, p =>
		p.RequireAuthenticatedUser()
		 .RequireAssertion(ctx =>
		 {
			 var t = ctx.User.FindFirst(AuthClaims.TokenType)?.Value;
			 return t == AuthClaims.UserTokenType || t == AuthClaims.OverlayTokenType;
		 }));

	// Fallback: every endpoint requires an authenticated user by default. Anonymous
	// endpoints must opt in with [AllowAnonymous].
	options.FallbackPolicy = new AuthorizationPolicyBuilder()
		.RequireAuthenticatedUser()
		.Build();
});

// ---- App services -----------------------------------------------------------
builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
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

builder.Services.AddSingleton<ITableStateManager, TableStateManager>();
builder.Services.AddSingleton<ITournamentStateManager, TournamentStateManager>();
builder.Services.AddHostedService<BreakTickService>();
builder.Services.AddSingleton<IHandEvaluator, HandEvaluator>();
builder.Services.AddSingleton<IEquityCalculator, EquityCalculator>();
builder.Services.AddSingleton<PokerAnalysisEngine>();
builder.Services.AddSingleton<IPokerAnalysisEngine>(sp => sp.GetRequiredService<PokerAnalysisEngine>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<PokerAnalysisEngine>());

builder.Services.Configure<RfidConfig>(builder.Configuration.GetSection(RfidConfig.SectionName));

builder.Services.AddSingleton<IRfidDeviceStore, RfidDeviceStore>();
builder.Services.AddSingleton<ICardTagMapper, CardTagMapper>();
builder.Services.AddSingleton<RfidReaderService>();
builder.Services.AddSingleton<IRfidReaderService>(sp => sp.GetRequiredService<RfidReaderService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<RfidReaderService>());
builder.Services.AddHostedService<IdleHandResetService>();
builder.Services.AddHostedService<MissingCardsAutoFoldService>();

// --- OBS camera director ----------------------------------------------------
// The Obs config section is kept as bootstrap defaults only; live settings come
// from ISettingsStore so operators can edit them from the admin UI at runtime.
builder.Services.Configure<RFIDPoker.Api.Models.ObsSettings>(
    builder.Configuration.GetSection(RFIDPoker.Api.Models.ObsSettings.SectionName));
builder.Services.AddScoped<ICameraRepository, CameraRepository>();
builder.Services.AddScoped<ISettingsStore, SettingsStore>();
builder.Services.AddSingleton<BroadcastState>();
builder.Services.AddSingleton<IBroadcastState>(sp => sp.GetRequiredService<BroadcastState>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<BroadcastState>());
builder.Services.AddSingleton<ObsClient>();
builder.Services.AddSingleton<IObsClient>(sp => sp.GetRequiredService<ObsClient>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<ObsClient>());
builder.Services.AddSingleton<CameraDirectorService>();
builder.Services.AddSingleton<ICameraDirector>(sp => sp.GetRequiredService<CameraDirectorService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<CameraDirectorService>());

builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IOverlayTokenService, OverlayTokenService>();
builder.Services.AddScoped<ITournamentDirectorTokenService, TournamentDirectorTokenService>();
builder.Services.AddSingleton<ITournamentDirectorState, TournamentDirectorState>();
builder.Services.AddSingleton<IManualTournamentState, ManualTournamentState>();
builder.Services.Configure<RFIDPoker.Api.Controllers.OverlayTokenSettings>(
    builder.Configuration.GetSection("OverlayToken"));

var app = builder.Build();

// ---- Apply migrations + seed roles -----------------------------------------
using (var scope = app.Services.CreateScope())
{
	var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
	db.Database.Migrate();

	var roleMgr = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
	foreach (var role in new[] { AuthRoles.Admin, AuthRoles.User })
	{
		if (!await roleMgr.RoleExistsAsync(role))
			await roleMgr.CreateAsync(new IdentityRole(role));
	}

	// Hydrate the TD enabled flag from the settings store into the singleton snapshot.
	var settingsStore = scope.ServiceProvider.GetRequiredService<ISettingsStore>();
	var tdState = scope.ServiceProvider.GetRequiredService<ITournamentDirectorState>();
	tdState.SetEnabled(await settingsStore.GetAsync(SettingKeys.TournamentDirectorEnabled, false));

	var manualTd = scope.ServiceProvider.GetRequiredService<IManualTournamentState>();
	manualTd.Set(await settingsStore.GetAsync(SettingKeys.ManualTournamentInfo, new ManualTournamentInfo()));

	// Hydrate the RFID device store from the DB. Operators manage the layout
	// entirely from the Config page; there is no appsettings fallback.
	var rfidStore = scope.ServiceProvider.GetRequiredService<IRfidDeviceStore>();
	await rfidStore.ReloadAsync();
}

if (app.Environment.IsDevelopment())
{
	app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<AnalysisHub>("/hubs/analysis");

// SPA fallback must be anonymous — the Angular shell (which renders /login) has to
// load before the user can authenticate. Route guards inside Angular still protect
// authenticated pages, and every API endpoint is protected server-side.
app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();

public record JwtOptions(string Issuer, string Audience, SymmetricSecurityKey Key);
