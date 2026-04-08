# Distributed Chat Platform

Distributed WhatsApp-like chat system built on .NET 10 and C# 13 with Clean Architecture, SOLID, CQRS, SignalR, RabbitMQ, Redis, PostgreSQL, Angular, and OpenTelemetry.

## English Summary

### Services

- API Gateway via YARP
- Identity Service with JWT auth, refresh tokens, BCrypt, CQRS, outbox, and user APIs
- Chat Service with SignalR, Redis backplane, CQRS write side, and RabbitMQ publication
- Message Service with PostgreSQL write/read models, outbox, projections, and query APIs
- Presence Service with Redis-backed CQRS presence tracking
- Notification Service with offline notification fan-out
- Angular frontend with standalone components, sign in, sign up, chat history queries, and SignalR commands

### Local Run

Build the .NET solution:

```bash
dotnet build Chat.slnx
```

Start the full local stack:

```bash
docker compose -f deploy/docker/docker-compose.yml up --build
```

Frontend becomes available on `http://localhost:4200` and the gateway on `http://localhost:8080`.

### Architecture Notes

- Commands are handled with MediatR on the write side.
- Query APIs are separated from writes and backed by read models.
- Identity and Message services use EF Core outbox tables with background dispatchers.
- MassTransit consumers are configured with retry, circuit breaker, and DLQ-compatible queues.
- Chat publishes events asynchronously with RabbitMQ and exposes realtime messaging through SignalR.
- Prometheus scrapes the `/metrics` endpoints exposed by the services.

### Migrations

Initial EF Core migrations were generated for:

- IdentityService.Infrastructure
- MessageService.Infrastructure

Identity and Message services run EF Core migrations automatically on startup.

## PT-BR

### Visão Geral

Plataforma de chat distribuída, no estilo WhatsApp, construída com .NET 10, C# 13, Clean Architecture, SOLID, CQRS, SignalR, RabbitMQ, Redis, PostgreSQL, Angular e OpenTelemetry.

### Serviços

- API Gateway com YARP
- Identity Service com autenticação JWT, refresh token, BCrypt, CQRS, outbox e APIs de usuários
- Chat Service com SignalR, Redis, lado de escrita em CQRS e publicação no RabbitMQ
- Message Service com modelos de escrita e leitura em PostgreSQL, outbox, projeções e APIs de consulta
- Presence Service com rastreamento de presença em Redis
- Notification Service com envio de notificações offline
- Frontend Angular com componentes standalone, login, cadastro, histórico de conversas e comandos em tempo real via SignalR

### Execução Local

Compile a solução .NET:

```bash
dotnet build Chat.slnx
```

Suba toda a stack local:

```bash
docker compose -f deploy/docker/docker-compose.yml up --build
```

Após a inicialização:

- Frontend: `http://localhost:4200`
- API Gateway: `http://localhost:8080`

### Notas de Arquitetura

- Os comandos são processados com MediatR no write side.
- As consultas ficam separadas da escrita e usam read models.
- Identity Service e Message Service usam outbox com tabelas do EF Core e despachantes em background.
- Os consumidores do MassTransit usam retry, circuit breaker e filas compatíveis com DLQ.
- O Chat Service publica eventos no RabbitMQ e mantém a comunicação em tempo real via SignalR.
- Os serviços expõem métricas que podem ser coletadas pelo Prometheus.

### Migrations

As migrations iniciais foram geradas para:

- IdentityService.Infrastructure
- MessageService.Infrastructure

As migrations desses dois serviços são executadas automaticamente no startup.

## Kubernetes Step By Step (PT-BR)

O manifesto principal está em `deploy/k8s/chat-platform.yaml`.

Importante: esse manifesto sobe a camada de aplicação, mas espera que as dependências de infraestrutura já existam no cluster com estes nomes de serviço dentro do namespace `chat-platform`:

- `postgres-identity`
- `postgres-message`
- `redis`
- `rabbitmq`
- `otel-collector`

Se esses serviços não existirem, os pods da aplicação não vão inicializar corretamente.

### 1. Pré-requisitos

Você precisa ter instalado:

- `kubectl`
- Um cluster Kubernetes funcional, como `kind`, `minikube`, Docker Desktop Kubernetes ou um cluster remoto
- `docker` para build das imagens
- Um registry acessível pelo cluster, caso você não use as imagens já publicadas

### 2. Crie ou selecione o cluster

Exemplo com `kind`:

```bash
kind create cluster --name chat-platform
kubectl cluster-info
```

Exemplo com `minikube`:

```bash
minikube start
kubectl cluster-info
```

### 3. Crie o namespace

```bash
kubectl create namespace chat-platform
```

Se ele já existir, o comando pode falhar sem problema. Nesse caso, siga para o próximo passo.

### 4. Provisione a infraestrutura base no namespace

Antes de aplicar a aplicação, garanta que existam no namespace `chat-platform`:

- PostgreSQL para identidade com service name `postgres-identity`
- PostgreSQL para mensagens com service name `postgres-message`
- Redis com service name `redis`
- RabbitMQ com service name `rabbitmq`
- OpenTelemetry Collector com service name `otel-collector`

Você pode subir esses componentes com Helm, manifests próprios ou operadores, desde que mantenha os mesmos nomes de serviço esperados pelo arquivo `deploy/k8s/chat-platform.yaml`.

Para conferir se a infraestrutura está pronta:

```bash
kubectl get svc -n chat-platform
```

Você deve ver pelo menos os serviços citados acima antes de seguir.

### 5. Gere e publique as imagens da aplicação

O manifesto atual referencia estas imagens:

- `bvilela/chat-identity:latest`
- `bvilela/chat-write:latest`
- `bvilela/chat-message:latest`
- `bvilela/chat-presence:latest`
- `bvilela/chat-notification:latest`
- `bvilela/chat-gateway:latest`
- `bvilela/chat-frontend:latest`

Se você usar outro registry ou outro namespace, altere os campos `image:` no arquivo `deploy/k8s/chat-platform.yaml`.

Exemplo de build local com as mesmas tags do manifesto:

```bash
docker build -t bvilela/chat-identity:latest -f src/IdentityService/API/Dockerfile .
docker build -t bvilela/chat-write:latest -f src/ChatService/API/Dockerfile .
docker build -t bvilela/chat-message:latest -f src/MessageService/API/Dockerfile .
docker build -t bvilela/chat-presence:latest -f src/PresenceService/API/Dockerfile .
docker build -t bvilela/chat-notification:latest -f src/NotificationService/API/Dockerfile .
docker build -t bvilela/chat-gateway:latest -f src/ApiGateway/Dockerfile .
docker build -t bvilela/chat-frontend:latest -f frontend/Dockerfile .
```

Se o seu cluster não conseguir puxar imagens locais, envie essas imagens para um registry acessível por ele.

### 6. Aplique o manifesto da aplicação

```bash
kubectl apply -f deploy/k8s/chat-platform.yaml
```

### 7. Aguarde os deployments ficarem prontos

```bash
kubectl get pods -n chat-platform
kubectl rollout status deployment/identity-service -n chat-platform
kubectl rollout status deployment/chat-service -n chat-platform
kubectl rollout status deployment/message-service -n chat-platform
kubectl rollout status deployment/presence-service -n chat-platform
kubectl rollout status deployment/notification-service -n chat-platform
kubectl rollout status deployment/api-gateway -n chat-platform
kubectl rollout status deployment/frontend -n chat-platform
```

Se algum pod falhar, inspecione com:

```bash
kubectl describe pod <pod-name> -n chat-platform
kubectl logs <pod-name> -n chat-platform
```

### 8. Exponha o frontend e o gateway

O manifesto define `frontend` e `api-gateway` como `LoadBalancer`. Em cloud providers isso pode ser suficiente. Em clusters locais, normalmente o jeito mais simples é usar `port-forward`:

```bash
kubectl port-forward svc/frontend 4200:80 -n chat-platform
kubectl port-forward svc/api-gateway 8080:80 -n chat-platform
```

Depois disso, acesse:

- Frontend: `http://localhost:4200`
- Gateway: `http://localhost:8080`

### 9. Observações importantes para ambiente local

- Os HPAs do manifesto usam métricas externas. Em clusters locais sem adapter de métricas externas, isso não impede a aplicação de subir, mas o autoscaling baseado nessas métricas não vai funcionar.
- Identity Service e Message Service aplicam migrations automaticamente quando iniciam.
- Se você trocar o namespace, os nomes de serviço ou as imagens, ajuste o manifesto antes do `kubectl apply`.

### 10. Limpeza do ambiente

Para remover a aplicação:

```bash
kubectl delete -f deploy/k8s/chat-platform.yaml
```

Se quiser remover também o namespace:

```bash
kubectl delete namespace chat-platform
```
