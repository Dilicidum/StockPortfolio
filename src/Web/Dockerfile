# =============================================================================
#  StockPortfolio SPA - built with Vite, served by nginx, /api proxied to the
#  API container.
#
#  Build context is the REPOSITORY ROOT (docker-compose.yml sets `context: .`)
#  so the single root .dockerignore governs what is sent, and node_modules and
#  dist from the host never leak into the image:
#      docker build -f src/Web/Dockerfile .
# =============================================================================

# Node 24 LTS. Vite 8 and the TypeScript 5.9 toolchain both want a recent
# Node; the tag is the LTS line, not `latest`, so a major bump is a deliberate
# edit here rather than a surprise on someone else's machine.
FROM node:24-alpine AS build
WORKDIR /src

# Manifest first so the install layer is cached until dependencies change.
# `npm ci` is the correct command and needs a committed package-lock.json;
# the fallback exists only so `docker compose up` from a clean clone cannot be
# broken by a missing lockfile - P0 req 7 outranks reproducible installs.
COPY src/Web/package*.json ./
RUN if [ -f package-lock.json ]; then \
        npm ci --no-audit --no-fund; \
    else \
        echo "WARNING: no package-lock.json - falling back to npm install" >&2; \
        npm install --no-audit --no-fund; \
    fi

COPY src/Web/ ./

# Vite reads VITE_-prefixed variables from the process environment at build
# time. Both MUST come from here rather than being hardcoded in vite.config.ts:
# nginx serves this bundle at /, so a baked-in `/<repo>/` base makes the page
# request /<repo>/assets/*.js and render blank. Only the GitHub Pages job
# passes a non-root base and an absolute API URL.
ARG VITE_BASE=/
ARG VITE_API_BASE_URL=
ENV VITE_BASE=${VITE_BASE}
ENV VITE_API_BASE_URL=${VITE_API_BASE_URL}

RUN npm run build


FROM nginx:1.29-alpine AS final

COPY src/Web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/dist /usr/share/nginx/html

EXPOSE 80

# nginx:alpine's own entrypoint/CMD is correct as-is; do not override it - it
# runs the envsubst templating and drops privileges for the workers.
