using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using CentralChat.API;
using CentralChat.Application;
using CentralChat.Infrastructure;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();
builder.Services.AddHttpContextAccessor(); builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter())); builder.Services.AddProblemDetails();
var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? throw new InvalidOperationException("JWT configuration is missing.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters { ValidateIssuer = true, ValidIssuer = jwt.Issuer, ValidateAudience = true, ValidAudience = jwt.Audience, ValidateLifetime = true, ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)), ClockSkew = TimeSpan.FromSeconds(30), NameClaimType = ClaimTypes.Name, RoleClaimType = ClaimTypes.Role };
    o.Events = new JwtBearerEvents { OnMessageReceived = context => { var token = context.Request.Query["access_token"]; if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs/communication")) context.Token = token; return Task.CompletedTask; } };
});
builder.Services.AddAuthorization(o =>
{
    foreach (var permission in new[] { Permissions.ContactsView, Permissions.TicketsView, Permissions.TicketsClaim, Permissions.TicketsAssign, Permissions.TicketsResolve, Permissions.MessagesView, Permissions.MessagesSend, Permissions.UsersManage, Permissions.SettingsManage })
        o.AddPolicy(permission, p => p.RequireClaim("permission", permission));
});
// SignalR serialises with its own options, so without this the hub sends enums as integers while the
// REST API sends them as strings — the same entity arriving in two different shapes.
var signalR = builder.Services.AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter())); var redis = builder.Configuration.GetConnectionString("Redis"); if (!string.IsNullOrWhiteSpace(redis)) signalR.AddStackExchangeRedis(redis);
builder.Services.AddRateLimiter(o =>
{
    o.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy("api", context => RateLimitPartition.GetTokenBucketLimiter(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new TokenBucketRateLimiterOptions { TokenLimit = 120, TokensPerPeriod = 120, ReplenishmentPeriod = TimeSpan.FromMinutes(1), AutoReplenishment = true }));
    o.AddPolicy("webhook", _ => RateLimitPartition.GetFixedWindowLimiter("meta", _ => new FixedWindowRateLimiterOptions { PermitLimit = 1000, Window = TimeSpan.FromMinutes(1), QueueLimit = 100 }));
});
builder.Services.AddEndpointsApiExplorer(); builder.Services.AddSwaggerGen(o => { o.SwaggerDoc("v1", new OpenApiInfo { Title = "CentralChat API", Version = "v1" }); o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" }); o.AddSecurityRequirement(new OpenApiSecurityRequirement { [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = [] }); });
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("postgresql", tags: ["ready"])
    .AddCheck<RedisHealthCheck>("redis", tags: ["ready"])
    .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["ready"]);
builder.Services.AddCors(o => o.AddPolicy("frontend", p => p.WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:3000"]).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();
app.UseMiddleware<ExceptionMiddleware>();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.UseCors("frontend"); app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization();
app.MapControllers().RequireRateLimiting("api"); app.MapHub<CommunicationHub>("/hubs/communication");
app.MapHealthChecks("/health"); app.MapHealthChecks("/health/ready"); app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
if (app.Configuration.GetValue<bool>("Database:ApplyMigrations")) await DatabaseInitializer.InitializeAsync(app.Services, app.Environment.IsDevelopment());
if (!app.Environment.IsDevelopment())
{
    var meta = app.Configuration.GetSection(MetaWhatsAppOptions.Section).Get<MetaWhatsAppOptions>() ?? new();
    var startup = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("CentralChat.API.Startup");
    if (!meta.ValidateSignature) startup.LogWarning("MetaWhatsApp:ValidateSignature is off outside Development; /webhook accepts unsigned payloads from anyone.");
    if (meta.UseDevelopmentClient) startup.LogWarning("MetaWhatsApp:UseDevelopmentClient is on outside Development; outbound replies are faked and never reach Meta.");
}
app.Run();

public partial class Program { }
