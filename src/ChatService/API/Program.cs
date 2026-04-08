using System.Text;
using ChatService.API.Hubs;
using ChatService.API.Middleware;
using ChatService.API.Services;
using ChatService.Application;
using ChatService.Infrastructure;
using FluentValidation;
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
builder.Services.AddSignalR()
    .AddStackExchangeRedis(builder.Configuration.GetConnectionString("Redis") ?? "redis:6379");
builder.Services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(SendMessageCommand).Assembly));
builder.Services.AddValidatorsFromAssembly(typeof(SendMessageCommand).Assembly);
builder.Services.AddChatInfrastructure(builder.Configuration);
builder.Services.AddScoped<IConversationNotifier, SignalRConversationNotifier>();

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

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrWhiteSpace(accessToken) && path.StartsWithSegments("/hubs/chat"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddHealthChecks();
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("chat-service"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource("MassTransit")
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter("ChatService")
        .AddPrometheusExporter()
        .AddOtlpExporter());

var app = builder.Build();

app.UseMiddleware<ChatExceptionMiddleware>();
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");
app.MapHealthChecks("/health");
app.MapPrometheusScrapingEndpoint();
app.Run();
