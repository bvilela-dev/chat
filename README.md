# Distributed Chat Platform

Distributed WhatsApp-like chat system built on .NET 10 and C# 13 with Clean Architecture, SOLID, CQRS, SignalR, RabbitMQ, Redis, PostgreSQL, Angular, and OpenTelemetry.

## Services

- API Gateway via YARP
- Identity Service with JWT auth, refresh tokens, BCrypt, CQRS, outbox, and gRPC user validation
- Chat Service with SignalR, Redis backplane, CQRS write side, and RabbitMQ publication
- Message Service with PostgreSQL write/read models, outbox, projections, and query APIs
- Presence Service with Redis-backed CQRS presence tracking
- Notification Service with offline notification fan-out
- Angular frontend with standalone components, login, chat history queries, and SignalR commands

## Local Run

Build the .NET solution:

```bash
dotnet build Chat.slnx
```

Start the full local stack:

```bash
docker compose -f deploy/docker/docker-compose.yml up --build
```

Frontend becomes available on `http://localhost:4200` and the gateway on `http://localhost:8080`.

## Architecture Notes

- Commands are handled with MediatR on the write side.
- Query APIs are separated from writes and backed by read models.
- Identity and Message services use EF Core outbox tables with background dispatchers.
- MassTransit consumers are configured with retry, circuit breaker, and DLQ-compatible queues.
- Chat validates users synchronously with gRPC and publishes events asynchronously with RabbitMQ.
- Prometheus scrapes the `/metrics` endpoints exposed by the services.

## Migrations

Initial EF Core migrations were generated for:

- IdentityService.Infrastructure
- MessageService.Infrastructure

## Frontend Note

The Angular app has been scaffolded manually in this environment because Node.js is not installed locally, so the frontend was not built or executed here.
