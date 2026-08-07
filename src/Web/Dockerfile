FROM node:24-alpine AS build
WORKDIR /src

COPY src/Web/package*.json ./
RUN if [ -f package-lock.json ]; then \
        npm ci --no-audit --no-fund; \
    else \
        echo "WARNING: no package-lock.json - falling back to npm install" >&2; \
        npm install --no-audit --no-fund; \
    fi

COPY src/Web/ ./

ARG VITE_BASE=/
ARG VITE_API_BASE_URL=
ENV VITE_BASE=${VITE_BASE}
ENV VITE_API_BASE_URL=${VITE_API_BASE_URL}

RUN npm run build

FROM nginx:1.29-alpine AS final

COPY src/Web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/dist /usr/share/nginx/html

EXPOSE 80
