# Readarr Dockerfile
# Multi-stage build: build in SDK image, run in runtime image

# ─────────────────────────────────────────────────────────────────────────────
# Stage 1: Build backend
# ─────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS backend-build

WORKDIR /src
COPY src/ ./

RUN dotnet restore Readarr.sln && \
    dotnet publish NzbDrone.Console/Readarr.Console.csproj \
      -c Release \
      -r linux-musl-x64 \
      --self-contained false \
      -f net10.0 \
      -o /app

# ─────────────────────────────────────────────────────────────────────────────
# Stage 2: Build frontend
# ─────────────────────────────────────────────────────────────────────────────
FROM node:20-alpine AS frontend-build

WORKDIR /build
COPY package.json yarn.lock .yarnrc tsconfig.json ./
COPY frontend/ ./frontend/

RUN yarn install --frozen-lockfile && \
    yarn build

# ─────────────────────────────────────────────────────────────────────────────
# Stage 3: Runtime
# ─────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine

# Install runtime dependencies for media processing
RUN apk add --no-cache \
    icu-libs \
    sqlite-libs \
    tzdata \
    curl \
    && rm -rf /var/cache/apk/*

# Enable globalization
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
ENV LC_ALL=en_US.UTF-8
ENV LANG=en_US.UTF-8

# Create readarr user
RUN addgroup -S readarr && adduser -S readarr -G readarr

# Application directory
WORKDIR /app
COPY --from=backend-build /app ./
COPY --from=frontend-build /build/_output/UI ./UI

# Config and data volumes
RUN mkdir -p /config /books /audiobooks && \
    chown -R readarr:readarr /app /config /books /audiobooks

USER readarr

EXPOSE 8787

VOLUME ["/config", "/books", "/audiobooks"]

HEALTHCHECK --interval=30s --timeout=10s --retries=3 \
    CMD curl -f http://localhost:8787/ping || exit 1

ENTRYPOINT ["dotnet", "Readarr.Console.dll"]
CMD ["--nobrowser", "--data=/config"]
