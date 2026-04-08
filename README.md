# Distributed Chat Platform

Distributed WhatsApp-like chat system built on .NET 10 and C# 13 with Clean Architecture, SOLID, CQRS, SignalR, RabbitMQ, Redis, PostgreSQL, Angular, and OpenTelemetry.

[English](#english-summary) | [PT-BR](#pt-br)

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

Se você estiver usando k3s, o fluxo recomendado agora é usar os scripts `./start.sh` e `./stop.sh` na raiz do projeto.

Eles usam exclusivamente o manifesto `deploy/k8s/chat-platform-k3s.yaml`, que já inclui:

- PostgreSQL de identidade
- PostgreSQL de mensagens
- Redis
- RabbitMQ
- OpenTelemetry Collector
- todos os serviços da aplicação
- Ingress via Traefik para o frontend

Os scripts fazem o seguinte:

- build das imagens Docker da aplicação
- import das imagens no `containerd` do k3s
- aplicação do manifesto Kubernetes
- espera dos rollouts até tudo ficar pronto
- criação automática da entrada `chat.local` no `/etc/hosts`

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

### 3. Inicie tudo com um único comando

```bash
chmod +x start.sh stop.sh
./start.sh
```

Se você quiser pular o rebuild ou o reimport das imagens em execuções seguintes:

```bash
SKIP_BUILD=1 SKIP_IMPORT=1 ./start.sh
```

### 4. Confira o estado do cluster

```bash
kubectl get pods -n chat-platform
kubectl get pvc -n chat-platform
kubectl get ingress -n chat-platform
```

Se algum pod falhar, inspecione com:

```bash
kubectl describe pod <pod-name> -n chat-platform
kubectl logs <pod-name> -n chat-platform
```

### 5. Acesse a aplicação

O script `start.sh` cria automaticamente a entrada `chat.local` no `/etc/hosts` com o IP do nó do k3s.

Depois acesse:

- Frontend: `http://chat.local`

O frontend já faz proxy para o gateway internamente, então você não precisa expor o `api-gateway` separadamente para o uso normal da aplicação.

Se quiser testar APIs ou o RabbitMQ manualmente, use `port-forward`:

```bash
kubectl port-forward svc/api-gateway 8080:8080 -n chat-platform
kubectl port-forward svc/rabbitmq 15672:15672 -n chat-platform
```

Depois disso, acesse:

- Gateway: `http://localhost:8080`
- RabbitMQ Management: `http://localhost:15672`

### 6. Observações importantes para ambiente local

- O manifesto `chat-platform-k3s.yaml` já inclui a infraestrutura mínima para rodar localmente no k3s.
- Os serviços da aplicação usam `imagePullPolicy: IfNotPresent`, então imagens importadas no `containerd` do k3s podem ser reutilizadas sem pull remoto.
- Identity Service e Message Service aplicam migrations automaticamente quando iniciam.
- O Ingress assume que o Traefik padrão do k3s está ativo.
- O frontend faz proxy interno para `api-gateway:8080`, então o `Service` do gateway dentro do cluster também precisa expor a porta `8080`.
- Se você trocar o namespace, os nomes de serviço, o host do Ingress ou as imagens, ajuste o manifesto antes do `kubectl apply`.

### 7. Derrube tudo

Para remover a aplicação:

```bash
./stop.sh
```
