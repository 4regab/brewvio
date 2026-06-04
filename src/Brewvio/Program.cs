using Brewvio.Data;
using Brewvio.Helpers;
using Brewvio.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Amazon.Lambda.AspNetCoreServer.Hosting;
using Npgsql;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Local-only secrets override (gitignored): appsettings.Development.local.json
// keeps real connection strings / keys out of source control.
builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.local.json",
    optional: true, reloadOnChange: true);

// DATABASE_URL is the Supabase pooler URL (postgres://user:pass@host:port/db).
// Build the Npgsql string safely (passwords are URL-encoded); SSL is required and
// prepared statements stay off (Max Auto Prepare=0 default) for the transaction pooler.
var connectionString = builder.Configuration.GetConnectionString("Default");
var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
if (!string.IsNullOrEmpty(databaseUrl))
{
    var uri = new Uri(databaseUrl);
    var creds = uri.UserInfo.Split(':', 2);
    connectionString = new NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.Port,
        Database = uri.AbsolutePath.TrimStart('/'),
        Username = Uri.UnescapeDataString(creds[0]),
        Password = creds.Length > 1 ? Uri.UnescapeDataString(creds[1]) : "",
        SslMode = SslMode.Require,
        // Supabase Supavisor transaction pooler does not support server-side prepared
        // statements; disabling them prevents "Timeout during reading attempt" hangs on writes.
        MaxAutoPrepare = 0,
        // Short connect timeout so cold-start DB issues fail fast instead of hanging the Lambda.
        Timeout = 15,
        CommandTimeout = 20
    }.ConnectionString;
}

builder.Services.AddControllers();
builder.Services.AddDbContext<BrewvioDbContext>(options =>
    options.UseNpgsql(connectionString));

// JWT auth: the app issues and validates its own HMAC-signed tokens (role claim = "role").
var jwtKey = Environment.GetEnvironmentVariable("JWT_KEY") ?? builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException(
        "JWT signing key not configured. Set the JWT_KEY environment variable (or Jwt:Key for local dev).");
// Application services (Controllers -> Services -> Data) + current-user accessor for auditing.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<MenuService>();
builder.Services.AddScoped<ShiftService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<ReportingService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<AuthService>();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;   // keep "sub"/"name"/"role" claim types exactly as issued
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            RoleClaimType = "role",
            NameClaimType = "name"
        };
    });
// Deny by default; endpoints opt out with [AllowAnonymous], roles with [Authorize(Roles="Manager")].
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// Runs as a normal Kestrel app locally and as an AWS Lambda behind API Gateway HTTP API.
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);

var app = builder.Build();

// Translate domain validation errors (thrown by services) into clean 400 responses.
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        await ctx.Response.WriteAsJsonAsync(new { message = ex.Message });
    }
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Seed demo data only outside Lambda. On Lambda the DB is already migrated/seeded,
// and running EF queries during cold-start init can stall the function.
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AWS_LAMBDA_FUNCTION_NAME")))
{
    await DatabaseInitializer.SeedAsync(app);
}

app.Run();
