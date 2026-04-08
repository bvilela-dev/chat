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

Se você estiver usando k3s, o manifesto recomendado agora é `deploy/k8s/chat-platform-k3s.yaml`.

Esse arquivo já inclui:

- PostgreSQL de identidade
- PostgreSQL de mensagens
- Redis
- RabbitMQ
- OpenTelemetry Collector
- todos os serviços da aplicação
- Ingress via Traefik para o frontend

O manifesto antigo `deploy/k8s/chat-platform.yaml` continua útil como base mais genérica, mas ele pressupõe infraestrutura externa já existente. Para k3s, use o arquivo `chat-platform-k3s.yaml`.

### 1. Pré-requisitos

Você precisa ter instalado:

- `kubectl`
- `k3s` funcional
- `docker` para build das imagens
- acesso com `sudo` no nó do k3s para importar imagens no `containerd`

Observações importantes para k3s:

- o manifesto usa PVCs simples, compatíveis com o `local-path-provisioner` padrão do k3s
- o acesso externo usa Ingress com classe `traefik`, que normalmente já vem habilitada no k3s

### 2. Verifique o cluster k3s

Confirme se o cluster está respondendo:

```bash
kubectl cluster-info
kubectl get nodes
```

Se o seu `kubectl` ainda não estiver apontando para o k3s, exporte o kubeconfig correto. Em instalações padrão:

```bash
export KUBECONFIG=/etc/rancher/k3s/k3s.yaml
```

### 3. Crie o namespace

```bash
kubectl create namespace chat-platform
```

Se ele já existir, o comando pode falhar sem problema. Nesse caso, siga para o próximo passo.

### 4. Gere as imagens da aplicação

O manifesto atual referencia estas imagens:

- `bvilela/chat-identity:latest`
- `bvilela/chat-write:latest`
- `bvilela/chat-message:latest`
- `bvilela/chat-presence:latest`
- `bvilela/chat-notification:latest`
- `bvilela/chat-gateway:latest`
- `bvilela/chat-frontend:latest`

Se você usar outro registry ou outro namespace, altere os campos `image:` no arquivo `deploy/k8s/chat-platform-k3s.yaml`.

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

### 5. Importe as imagens no k3s

Como o k3s usa `containerd`, imagens buildadas no Docker local não ficam automaticamente disponíveis para o cluster.

No próprio nó do k3s, importe as imagens assim:

```bash
docker save \
	bvilela/chat-identity:latest \
	bvilela/chat-write:latest \
	bvilela/chat-message:latest \
	bvilela/chat-presence:latest \
	bvilela/chat-notification:latest \
	bvilela/chat-gateway:latest \
	bvilela/chat-frontend:latest \
	| sudo k3s ctr images import -
```

Se você estiver buildando em outra máquina que não é o nó do k3s, o caminho mais estável é publicar as imagens em um registry acessível pelo cluster.

### 6. Aplique o manifesto do k3s

```bash
kubectl apply -f deploy/k8s/chat-platform-k3s.yaml
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
kubectl get pvc -n chat-platform
```

Se algum pod falhar, inspecione com:

```bash
kubectl describe pod <pod-name> -n chat-platform
kubectl logs <pod-name> -n chat-platform
```

### 8. Exponha o frontend no k3s

O manifesto do k3s usa Ingress com Traefik e expõe o frontend no host `chat.local`.

Adicione uma entrada no seu `/etc/hosts` apontando para o IP do nó do k3s:

```bash
<IP_DO_NO_K3S> chat.local
```

Depois acesse:

- Frontend: `http://chat.local`

O frontend já faz proxy para o gateway internamente, então você não precisa expor o `api-gateway` separadamente para o uso normal da aplicação.

Se quiser testar APIs ou o RabbitMQ manualmente, use `port-forward`:

```bash
kubectl port-forward svc/api-gateway 8080:80 -n chat-platform
kubectl port-forward svc/rabbitmq 15672:15672 -n chat-platform
```

Depois disso, acesse:

- Gateway: `http://localhost:8080`
- RabbitMQ Management: `http://localhost:15672`

### 9. Observações importantes para ambiente local

- O manifesto `chat-platform-k3s.yaml` já inclui a infraestrutura mínima para rodar localmente no k3s.
- Os serviços da aplicação usam `imagePullPolicy: IfNotPresent`, então imagens importadas no `containerd` do k3s podem ser reutilizadas sem pull remoto.
- Identity Service e Message Service aplicam migrations automaticamente quando iniciam.
- O Ingress assume que o Traefik padrão do k3s está ativo.
- Se você trocar o namespace, os nomes de serviço, o host do Ingress ou as imagens, ajuste o manifesto antes do `kubectl apply`.

### 10. Limpeza do ambiente

Para remover a aplicação:

```bash
kubectl delete -f deploy/k8s/chat-platform-k3s.yaml
```

Se quiser remover também o namespace:

```bash
kubectl delete namespace chat-platform
```
