FROM node:22-alpine AS build
WORKDIR /app
COPY frontend/package.json ./package.json
RUN npm install
COPY frontend/ .
RUN npm run build

FROM nginx:1.27-alpine
COPY deploy/docker/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/chat-frontend /usr/share/nginx/html
EXPOSE 80