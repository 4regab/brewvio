using Brewvio.Data;
using Brewvio.Helpers;
using Brewvio.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Amazon.Lambda.AspNetCoreServer.Hosting;
using Amazon.Lambda.AspNetCoreServer;
using Npgsql;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Local-only secrets override (gitignored): appsettings.Development.local.json
// keeps real connection strings / keys out of source control.
builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.local.json",
    optional: true, reloadOnChange: true);

// On Lambda, load secrets (DATABASE_URL, JWT_KEY) from SSM Parameter Store under the
// /brewvio path instead of plaintext Lambda env vars. SecureString params are decrypted
// via KMS using the function's role. Path-based loading maps /brewvio/DATABASE_URL ->
// config key "DATABASE_URL" and /brewvio/JWT_KEY -> "JWT_KEY". Added last, so it takes
// precedence over any env vars. Required (optional:false) so misconfiguration fails fast.
if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AWS_LAMBDA_FUNCTION_NAME")))
{
    var ssmPath = Environment.GetEnvironmentVariable("SSM_PARAMETER_PATH") ?? "/brewvio";
    builder.Configuration.AddSystemsManager(ssmPath, optional: false);
}

// DATABASE_URL is the Supabase pooler URL (postgres://user:pass@host:port/db). Read via
// configuration so it resolves from an env var locally or from SSM on Lambda.
// Build the Npgsql string safely (passwords are URL-encoded); SSL is required and
// prepared statements stay off (Max Auto Prepare=0 default) for the transaction pooler.
var connectionString = builder.Configuration.GetConnectionString("Default");
var databaseUrl = builder.Configuration["DATABASE_URL"];
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
        // Disable Npgsql's CLIENT-side pool. On Lambda the process is frozen between invocations,
        // so a pooled connection goes stale (Supavisor reclaims the server side) and the next
        // query hangs until CommandTimeout -> "Timeout during reading attempt" 22s stalls. Supavisor
        // IS the connection pool, so each invocation opening a fresh connection to it is the intended
        // serverless pattern and removes the stale-socket hang entirely.
        Pooling = false,
        // Fail fast on a stalled pooler connection so a bad connect surfaces quickly
        // instead of hanging the synchronous request (the retry policy below adds bounded retries).
        Timeout = 8,
        CommandTimeout = 20
    }.ConnectionString;
}

builder.Services.AddControllers();
builder.Services.AddDbContext<BrewvioDbContext>(options =>
    // EnableRetryOnFailure: retries genuine transient pooler/network drops instead of surfacing
    // them as 500s. Kept deliberately small (2 retries, 2s max backoff) so a slow cold-start
    // connection can't stack into a 20s+ synchronous wait that blows past the 30s API Gateway
    // timeout. Multi-step writes use the provider's execution strategy (see OrderService) so they
    // compose with the retry policy.
    options.UseNpgsql(connectionString, npgsql =>
        npgsql.EnableRetryOnFailure(
            maxRetryCount: 2,
            maxRetryDelay: TimeSpan.FromSeconds(2),
            errorCodesToAdd: null)));

// JWT auth: the app issues and validates its own HMAC-signed tokens (role claim = "role").
// Read via configuration so the key resolves from JWT_KEY (env var locally / SSM on Lambda)
// or Jwt:Key (local dev appsettings). The app throws on startup if none is configured.
var jwtKey = builder.Configuration["JWT_KEY"] ?? builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException(
        "JWT signing key not configured. Set the JWT_KEY environment variable (or Jwt:Key for local dev).");
// Application services (Controllers -> Services -> Data) + current-user accessor for auditing.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<MenuService>();
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
// API Gateway returns the response body as a UTF-8 string unless the content type is
// registered as binary — in which case the library Base64-encodes it and sets
// isBase64Encoded=true. PDF and images are binary by default, but the XLSX MIME type is
// not, so without this its zip bytes are mangled by UTF-8 encoding and Excel reports the
// downloaded file as corrupt. Register it as Base64 so spreadsheet downloads arrive intact.
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi, options =>
    options.RegisterResponseContentEncodingForContentType(
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ResponseContentEncoding.Base64));

var app = builder.Build();

// Correlation id + structured request logging. Every request gets a trace id (reusing an
// inbound X-Correlation-Id / X-Amzn-Trace-Id when present) that flows into a logging scope, the
// response header, and any error payload — so a single id ties together all logs for a request.
app.Use(async (ctx, next) =>
{
    var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Brewvio.Request");
    var correlationId = ctx.Request.Headers["X-Correlation-Id"].FirstOrDefault()
        ?? ctx.Request.Headers["X-Amzn-Trace-Id"].FirstOrDefault()
        ?? Guid.NewGuid().ToString("N");
    ctx.Response.Headers["X-Correlation-Id"] = correlationId;

    using var scope = logger.BeginScope(new Dictionary<string, object>
    {
        ["CorrelationId"] = correlationId,
        ["Method"] = ctx.Request.Method,
        ["Path"] = ctx.Request.Path.Value ?? "",
    });
    var startedAt = DateTime.UtcNow;
    try
    {
        await next();
        var elapsedMs = (DateTime.UtcNow - startedAt).TotalMilliseconds;
        logger.LogInformation("{Method} {Path} responded {StatusCode} in {ElapsedMs:0}ms",
            ctx.Request.Method, ctx.Request.Path.Value, ctx.Response.StatusCode, elapsedMs);
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
    {
        // Expected domain validation errors -> clean 400, logged at Warning (not an outage signal).
        logger.LogWarning(ex, "Validation error on {Method} {Path}", ctx.Request.Method, ctx.Request.Path.Value);
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        await ctx.Response.WriteAsJsonAsync(new { message = ex.Message, correlationId });
    }
    catch (Exception ex)
    {
        // Unexpected errors -> 500, logged at Error so they trip CloudWatch error alarms; the
        // correlation id is returned so a user can quote it without leaking internals.
        logger.LogError(ex, "Unhandled error on {Method} {Path}", ctx.Request.Method, ctx.Request.Path.Value);
        ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await ctx.Response.WriteAsJsonAsync(new { message = "An unexpected error occurred.", correlationId });
    }
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// --seed-sales: seed 3 months of sales into the live DB then exit (no web server started).
// --force-seed-sales: same but skips the "transactions already exist" guard (for re-seeding).
if (args.Contains("--seed-sales") || args.Contains("--force-seed-sales"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<BrewvioDbContext>();
    bool force = args.Contains("--force-seed-sales");
    app.Logger.LogInformation("Running SeedSalesAsync against live DB (force={Force})…", force);
    if (force)
        await DatabaseInitializer.SeedSalesAsync(db, force: true);
    else
        await DatabaseInitializer.SeedSalesAsync(db);
    app.Logger.LogInformation("SeedSalesAsync complete.");
    return;
}

// Seed demo data only outside Lambda. On Lambda the DB is already migrated/seeded,
// and running EF queries during cold-start init can stall the function.
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AWS_LAMBDA_FUNCTION_NAME")))
{
    await DatabaseInitializer.SeedAsync(app);
}

await app.RunAsync();
