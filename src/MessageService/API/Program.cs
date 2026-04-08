using System.Text;
using AutoMapper;
using FluentValidation;
using MediatR;
using MessageService.API.Middleware;
using MessageService.Application;
using MessageService.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
var jwtKey = builder.Configuration["Jwt:Key"] ?? "super-secret-development-key-change-me";

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<IMapper>(_ => new MapperConfiguration(configuration => configuration.AddMaps(typeof(MessageMappingProfile).Assembly)).CreateMapper());
builder.Services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(GetMessagesByConversationQuery).Assembly));
builder.Services.AddValidatorsFromAssembly(typeof(GetMessagesByConversationQuery).Assembly);
builder.Services.AddMessageInfrastructure(builder.Configuration);
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
builder.Services.AddHealthChecks();
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("message-service"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource("MassTransit")
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter("MessageService")
        .AddPrometheusExporter()
        .AddOtlpExporter());

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<MessageDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.UseMiddleware<MessageExceptionMiddleware>();
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");
app.MapPrometheusScrapingEndpoint();
app.Run();
