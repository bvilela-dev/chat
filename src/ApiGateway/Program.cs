using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
var jwtKey = builder.Configuration["Jwt:Key"] ?? "super-secret-development-key-change-me";

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
	.AddJwtBearer(options =>
	{
		options.TokenValidationParameters = new TokenValidationParameters
		{
			ValidateIssuer = true,
			ValidateAudience = true,
			ValidateLifetime = true,
			ValidateIssuerSigningKey = true,
			ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "chat-identity",
			ValidAudience = builder.Configuration["Jwt:Audience"] ?? "chat-clients",
			IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
		};
	});
builder.Services.AddAuthorization();
builder.Services.AddCors(options =>
{
	options.AddPolicy("frontend", policy =>
	{
		policy.AllowAnyHeader()
			.AllowAnyMethod()
			.AllowCredentials()
			.SetIsOriginAllowed(_ => true);
	});
});
builder.Services.AddHealthChecks();
builder.Services.AddOpenTelemetry()
	.ConfigureResource(resource => resource.AddService("api-gateway"))
	.WithTracing(tracing => tracing
		.AddAspNetCoreInstrumentation()
		.AddHttpClientInstrumentation()
		.AddOtlpExporter())
	.WithMetrics(metrics => metrics
		.AddAspNetCoreInstrumentation()
		.AddRuntimeInstrumentation()
		.AddPrometheusExporter()
		.AddOtlpExporter());

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapReverseProxy();
app.MapHealthChecks("/health");
app.MapPrometheusScrapingEndpoint();
app.Run();
