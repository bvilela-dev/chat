# =============================================================================
# DOCKERFILE ÚNICO E PARAMETRIZADO PARA TODOS OS SERVIÇOS .NET
#
# Antes havia seis Dockerfiles quase idênticos, um por serviço. Além da
# duplicação, isso é um risco operacional: uma melhoria de segurança (rodar como
# usuário não-root, por exemplo) precisava ser replicada seis vezes, e bastava
# esquecer uma para o serviço ficar diferente dos demais.
#
# O argumento SERVICE_PATH seleciona qual projeto compilar.
# =============================================================================

ARG SERVICE_PATH
ARG SERVICE_DLL

# -----------------------------------------------------------------------------
# ESTÁGIO 1 — restore
#
# Copiamos APENAS os arquivos de projeto antes de copiar o código-fonte.
#
# O motivo é cache de camadas: o Docker reaproveita uma camada enquanto os
# arquivos que ela usa não mudam. Como dependências mudam raramente e o código
# muda a cada commit, isolar o restore faz com que a etapa mais lenta do build
# (baixar pacotes do NuGet) seja executada apenas quando um .csproj realmente
# mudar. Na prática, corta minutos de cada build.
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
ARG SERVICE_PATH
WORKDIR /src

# Arquivos que definem as versões dos pacotes para toda a solução.
COPY Directory.Build.props Directory.Packages.props global.json ./

# Estrutura de projetos, sem o código-fonte.
COPY src/BuildingBlocks/Application/*.csproj              src/BuildingBlocks/Application/
COPY src/BuildingBlocks/AspNetCore/*.csproj               src/BuildingBlocks/AspNetCore/
COPY src/BuildingBlocks/Contracts/*.csproj                src/BuildingBlocks/Contracts/
COPY src/BuildingBlocks/Messaging/*.csproj                src/BuildingBlocks/Messaging/
COPY src/ApiGateway/*.csproj                              src/ApiGateway/
COPY src/ChatService/API/*.csproj                         src/ChatService/API/
COPY src/ChatService/Application/*.csproj                 src/ChatService/Application/
COPY src/ChatService/Domain/*.csproj                      src/ChatService/Domain/
COPY src/ChatService/Infrastructure/*.csproj              src/ChatService/Infrastructure/
COPY src/IdentityService/API/*.csproj                     src/IdentityService/API/
COPY src/IdentityService/Application/*.csproj             src/IdentityService/Application/
COPY src/IdentityService/Domain/*.csproj                  src/IdentityService/Domain/
COPY src/IdentityService/Infrastructure/*.csproj          src/IdentityService/Infrastructure/
COPY src/MessageService/API/*.csproj                      src/MessageService/API/
COPY src/MessageService/Application/*.csproj              src/MessageService/Application/
COPY src/MessageService/Domain/*.csproj                   src/MessageService/Domain/
COPY src/MessageService/Infrastructure/*.csproj           src/MessageService/Infrastructure/
COPY src/NotificationService/API/*.csproj                 src/NotificationService/API/
COPY src/NotificationService/Application/*.csproj         src/NotificationService/Application/
COPY src/NotificationService/Infrastructure/*.csproj      src/NotificationService/Infrastructure/
COPY src/PresenceService/API/*.csproj                     src/PresenceService/API/
COPY src/PresenceService/Application/*.csproj             src/PresenceService/Application/
COPY src/PresenceService/Domain/*.csproj                  src/PresenceService/Domain/
COPY src/PresenceService/Infrastructure/*.csproj          src/PresenceService/Infrastructure/

RUN dotnet restore "${SERVICE_PATH}"

# -----------------------------------------------------------------------------
# ESTÁGIO 2 — publish
# -----------------------------------------------------------------------------
FROM restore AS publish
ARG SERVICE_PATH

# Agora sim o código-fonte. Esta camada é invalidada a cada commit, mas as
# dependências já estão em cache no estágio anterior.
COPY src/ src/

RUN dotnet publish "${SERVICE_PATH}" \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    # UseAppHost=false não gera o executável nativo: o contêiner sempre invoca
    # `dotnet App.dll`, então o binário nativo seria peso morto na imagem.
    /p:UseAppHost=false

# -----------------------------------------------------------------------------
# ESTÁGIO 3 — runtime
#
# Imagem final baseada apenas no runtime ASP.NET (~110 MB), e não no SDK
# (~800 MB). Além do tamanho, é uma questão de superfície de ataque: a imagem de
# produção não carrega compilador, ferramentas de build nem código-fonte.
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ARG SERVICE_DLL

# curl para o HEALTHCHECK do contêiner.
#
# A imagem `aspnet` é deliberadamente enxuta e não traz curl nem wget — o que é
# correto do ponto de vista de superfície de ataque, mas impede qualquer
# health check que precise falar HTTP de dentro do contêiner.
#
# Vale registrar que isto serve ao Docker Compose local. No Kubernetes, as
# probes `httpGet` são executadas pelo KUBELET, de fora do contêiner, e não
# exigem cliente HTTP nenhum lá dentro — é a razão de as probes do manifesto k8s
# funcionarem sem esta dependência.
#
# `--no-install-recommends` e a limpeza do cache do apt na MESMA instrução RUN:
# cada instrução vira uma camada, então remover os arquivos numa camada
# posterior não reduziria o tamanho final da imagem.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=publish /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SERVICE_DLL=${SERVICE_DLL}

# USUÁRIO NÃO-ROOT.
#
# As imagens oficiais do .NET já trazem o usuário `app` (UID 1654). Rodar como
# root dentro do contêiner significa que uma falha de execução remota de código
# na aplicação começa com privilégios administrativos no contêiner — e passa a
# depender apenas de uma falha de escape para comprometer o host.
#
# É também exigência de qualquer PodSecurityStandard "restricted" no Kubernetes.
USER app

# 8080 = REST/HTTP1 · 8081 = gRPC/HTTP2 (só Identity e Message escutam nela).
# EXPOSE é documental — não publica nada por si só —, mas deixa a topologia de
# portas visível para quem for ler o Dockerfile.
EXPOSE 8080
EXPOSE 8081

# Health check da imagem: permite ao Docker Compose (e ao `docker ps`) distinguir
# "contêiner iniciado" de "aplicação pronta para receber tráfego". É o que
# sustenta o `condition: service_healthy` das dependências no compose.
#
# `start-period` de 40 s cobre a aplicação de migrations no startup sem que as
# tentativas iniciais contem como falha.
HEALTHCHECK --interval=15s --timeout=5s --start-period=40s --retries=5 \
    CMD curl --fail --silent http://localhost:8080/health || exit 1

# ENTRYPOINT em forma de EXEC (array JSON), e não em forma de shell.
#
# A diferença é operacionalmente importante. Na forma de shell
# (`ENTRYPOINT dotnet $VAR`), o Docker executa `/bin/sh -c "..."`, e o shell
# vira o PID 1. O processo .NET fica sendo filho dele — e o `docker stop` /
# `SIGTERM` do Kubernetes é entregue ao SHELL, que não o repassa.
#
# O efeito prático: a aplicação nunca recebe o sinal de encerramento, não executa
# o shutdown gracioso do ASP.NET Core e é morta com SIGKILL após o grace period
# (30 s por padrão no Kubernetes). Num serviço de chat, isso significa derrubar
# conexões WebSocket abertas de forma abrupta, em vez de encerrá-las
# ordenadamente, e abandonar mensagens em processamento.
#
# `sh -c` com `exec` resolve os dois lados: ainda expande a variável com o nome
# da DLL (que varia por serviço), mas o `exec` SUBSTITUI o processo do shell pelo
# do .NET — que assume o PID 1 e passa a receber os sinais diretamente.
ENTRYPOINT ["/bin/sh", "-c", "exec dotnet \"$DOTNET_SERVICE_DLL\""]
