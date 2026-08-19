# =============================================================================
# FRONTEND ANGULAR — build multi-estágio servido por nginx.
# =============================================================================

# -----------------------------------------------------------------------------
# ESTÁGIO 1 — build
# -----------------------------------------------------------------------------
FROM node:22-alpine AS build
WORKDIR /app

# package.json E package-lock.json juntos, antes do código-fonte.
#
# Duas razões:
#  1. Cache de camadas: `npm ci` só reexecuta quando as dependências mudam.
#  2. Builds reproduzíveis: `npm ci` instala EXATAMENTE as versões travadas no
#     lockfile. O `npm install` da versão anterior podia resolver versões
#     diferentes a cada build — e o lockfile sequer era copiado para a imagem,
#     tornando o resultado imprevisível entre a máquina local e o CI.
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci

COPY frontend/ .

# Build de produção: minificação, tree-shaking, hash nos nomes de arquivo e
# verificação dos budgets de tamanho definidos no angular.json.
RUN npm run build

# -----------------------------------------------------------------------------
# ESTÁGIO 2 — runtime
#
# Só os arquivos estáticos gerados. Node, node_modules e código-fonte ficam para
# trás: a imagem final tem ~50 MB, contra ~1 GB se o estágio de build fosse
# publicado.
# -----------------------------------------------------------------------------
FROM nginx:1.27-alpine AS runtime

COPY deploy/docker/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/chat-frontend /usr/share/nginx/html

# Health check próprio da imagem: o orquestrador consegue distinguir
# "iniciando" de "pronto para receber tráfego".
#
# O endereço é 127.0.0.1, e não `localhost`, de propósito. Dentro do contêiner,
# `localhost` resolve primeiro para o IPv6 `::1`, enquanto a diretiva `listen 80`
# do nginx faz bind apenas em IPv4 — o resultado é "Connection refused" e um
# contêiner permanentemente marcado como unhealthy, embora esteja servindo
# normalmente. Forçar IPv4 elimina a ambiguidade.
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD wget --quiet --tries=1 --spider http://127.0.0.1/ || exit 1

EXPOSE 80
