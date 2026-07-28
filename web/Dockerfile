# sonarr-web — the Next.js panel (docs/09-web-panels.md).
# Build from THIS directory: docker build -t sonarr-web:local web/

FROM node:22-alpine AS build
WORKDIR /src

# Manifests first so `npm ci` is cached until a dependency changes.
COPY package.json package-lock.json ./
RUN npm ci

COPY . .
# The build renders nothing server-side (every route is dynamic), so it needs no API reachable.
RUN npm run build

# --- runtime: the traced standalone output only, no npm, no full dependency tree ---
FROM node:22-alpine AS runtime
WORKDIR /app
ENV NODE_ENV=production

# `node` (uid 1000) ships with the image. Nothing here is written at runtime, so root-owned
# files copied in are fine — they only need to be readable.
# static/ is served by server.js but is not traced into standalone, hence the second COPY;
# miss it and every page loads with no CSS and no chunks. There is no public/ — the panel
# ships no static assets of its own (favicon lives in app/).
COPY --from=build /src/.next/standalone ./
COPY --from=build /src/.next/static ./.next/static
USER node

# The bot runs with `network_mode: host` (docs/11), so from inside this bridged container the
# API is the host, not 127.0.0.1. Compose passes the real value; this default is for a bare
# `docker run` on the same host.
ENV SONARR_API_URL=http://host.docker.internal:5088 \
    HOSTNAME=0.0.0.0 \
    PORT=3000

EXPOSE 3000

CMD ["node", "server.js"]
