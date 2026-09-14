using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using TeamPilot.API.Auth;
using TeamPilot.API.Middleware;
using TeamPilot.API.Swagger;
using TeamPilot.Application.Common.Interfaces;
using TeamPilot.Application.DependencyInjection;
using TeamPilot.Infrastructure.Auth;
using TeamPilot.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SchemaFilter<EnumSchemaFilter>();
    // Adds the "Authorize" button to Swagger UI so a bearer token can be attached to
    // try-it-out requests; Swashbuckle applies it globally once entered.
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT access token: \"Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
    });

    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

const string AngularClientCorsPolicy = "AngularClient";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    // AllowCredentials requires explicit origins (no AllowAnyOrigin) because the
    // refresh-token cookie flow (/api/auth/refresh) relies on credentialed requests.
    options.AddPolicy(AngularClientCorsPolicy, policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

builder.Services.AddHttpContextAccessor();

// HttpContext is only ever null here when this resolves outside a live HTTP request - i.e. a
// detached background scope (IBackgroundTaskRunner) - never during normal request handling,
// since IHttpContextAccessor's ambient HttpContext is set for the whole pipeline. That guarantee
// only holds because BackgroundTaskRunner.Run explicitly suppresses ExecutionContext flow before
// its Task.Run - without that, the AsyncLocal behind IHttpContextAccessor would flow into the
// "detached" task anyway and this would wrongly pick HttpContextCurrentUserContext there. See
// SystemCurrentUserContext for why that case needs a distinct identity rather than just falling
// through as "unauthenticated" from HttpContextCurrentUserContext itself.
builder.Services.AddScoped<ICurrentUserContext>(sp =>
{
    var httpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>();
    return httpContextAccessor.HttpContext is not null
        ? new HttpContextCurrentUserContext(httpContextAccessor)
        : new SystemCurrentUserContext();
});

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

// HS256 requires a key of at least 256 bits (32 bytes); failing fast here avoids a key that's
// silently padded (or otherwise mismatched) between signing and validation.
if (Encoding.UTF8.GetByteCount(jwtOptions.SigningKey) < 32)
{
    throw new InvalidOperationException(
        $"{JwtOptions.SectionName}:{nameof(JwtOptions.SigningKey)} must be at least 32 characters " +
        "(HS256 requires a 256-bit key). Set it via `dotnet user-secrets set \"Jwt:SigningKey\" \"<value>\"`.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep claim types exactly as AccessTokenGenerator issued them (no "sub" ->
        // ClaimTypes.NameIdentifier remapping) - see HttpContextCurrentUserContext.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseExceptionHandler();
app.UseCors(AngularClientCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

// Every endpoint requires a valid access token by default; [AllowAnonymous] on
// AuthController's login/refresh/logout actions opts out explicitly.
app.MapControllers().RequireAuthorization();

app.Run();
