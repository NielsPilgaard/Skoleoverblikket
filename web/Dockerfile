FROM node:20-alpine AS build
WORKDIR /app

COPY web/package.json web/package-lock.json ./
RUN npm ci

# openapi-typescript codegen reads from ../openapi/ relative to web/
COPY openapi/ ../openapi/
COPY web/ .
ARG VITE_KEYCLOAK_URL=http://localhost:8080
ENV VITE_KEYCLOAK_URL=$VITE_KEYCLOAK_URL
RUN npm run build

FROM nginx:alpine AS runtime
COPY --from=build /app/dist /usr/share/nginx/html
COPY web/nginx.conf /etc/nginx/conf.d/default.conf
# Writes /env.js from KEYCLOAK_URL at start, overriding the build-time VITE_KEYCLOAK_URL.
COPY --chmod=755 web/runtime-config.sh /docker-entrypoint.d/40-runtime-config.sh
EXPOSE 80
