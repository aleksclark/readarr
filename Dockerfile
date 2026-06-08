# Readarr Revitalized — Docker Image
# Drop-in compatible with linuxserver/readarr (s6-overlay, PUID/PGID, /config)
#
# Multi-stage build:
#   1. Build backend (.NET 10, self-contained linux-musl-x64)
#   2. Build frontend (Node 20, webpack)
#   3. Runtime (Alpine + s6-overlay, mirrors linuxserver structure)

# ─────────────────────────────────────────────────────────────────────────────
# Stage 1: Build backend (self-contained — no runtime needed in final image)
# ─────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS backend-build

WORKDIR /src
COPY src/ ./
COPY global.json /
COPY Logo/ /Logo/

RUN dotnet restore Readarr.sln -p:EnableAnalyzers=false && \
    dotnet publish NzbDrone.Console/Readarr.Console.csproj \
      -c Release \
      -r linux-musl-x64 \
      --self-contained true \
      -f net10.0 \
      -p:PublishSingleFile=false \
      -p:PublishTrimmed=false \
      -p:EnableAnalyzers=false \
      -p:EnforceCodeStyleInBuild=false \
      -p:TreatWarningsAsErrors=false \
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
# Stage 3: Runtime — linuxserver/hotio compatible
# ─────────────────────────────────────────────────────────────────────────────
FROM alpine:3.21

ARG S6_OVERLAY_VERSION=3.2.0.2

# Install s6-overlay
ADD https://github.com/just-containers/s6-overlay/releases/download/v${S6_OVERLAY_VERSION}/s6-overlay-noarch.tar.xz /tmp/
ADD https://github.com/just-containers/s6-overlay/releases/download/v${S6_OVERLAY_VERSION}/s6-overlay-x86_64.tar.xz /tmp/
RUN tar -C / -Jxpf /tmp/s6-overlay-noarch.tar.xz && \
    tar -C / -Jxpf /tmp/s6-overlay-x86_64.tar.xz && \
    rm -f /tmp/s6-overlay-*.tar.xz

# Install runtime dependencies
RUN apk add --no-cache \
    bash \
    ca-certificates \
    curl \
    icu-libs \
    jq \
    libgcc \
    libintl \
    libstdc++ \
    shadow \
    sqlite-libs \
    tzdata \
    && rm -rf /var/cache/apk/*

# Create abc user (linuxserver convention)
RUN groupadd -g 1000 abc && \
    useradd -u 1000 -g abc -d /config -s /bin/bash abc && \
    mkdir -p /app/readarr/bin /config /books /audiobooks /run/readarr-temp

# Environment variables (linuxserver/hotio compatible)
ENV HOME=/config \
    XDG_CONFIG_HOME=/config/xdg \
    COMPlus_EnableDiagnostics=0 \
    TMPDIR=/run/readarr-temp \
    S6_CMD_WAIT_FOR_SERVICES_MAXTIME=0 \
    S6_VERBOSITY=1

# Copy application
COPY --from=backend-build /app /app/readarr/bin/
COPY --from=frontend-build /build/_output/UI /app/readarr/bin/UI/

# s6 service definition — mirrors linuxserver structure
RUN mkdir -p /etc/s6-overlay/s6-rc.d/init-readarr/dependencies.d \
             /etc/s6-overlay/s6-rc.d/svc-readarr/dependencies.d \
             /etc/s6-overlay/s6-rc.d/user/contents.d

# init script: set up PUID/PGID/UMASK
RUN printf '#!/command/with-contenv bash\n\
PUID=${PUID:-1000}\n\
PGID=${PGID:-1000}\n\
UMASK=${UMASK:-002}\n\
\n\
groupmod -o -g "$PGID" abc 2>/dev/null\n\
usermod -o -u "$PUID" abc 2>/dev/null\n\
\n\
umask "$UMASK"\n\
\n\
chown abc:abc /config /run/readarr-temp\n\
chown -R abc:abc /app/readarr\n\
' > /etc/s6-overlay/s6-rc.d/init-readarr/run && \
    chmod +x /etc/s6-overlay/s6-rc.d/init-readarr/run && \
    echo "oneshot" > /etc/s6-overlay/s6-rc.d/init-readarr/type && \
    touch /etc/s6-overlay/s6-rc.d/init-readarr/up && \
    printf '/etc/s6-overlay/s6-rc.d/init-readarr/run\n' > /etc/s6-overlay/s6-rc.d/init-readarr/up

# service run script
RUN printf '#!/command/with-contenv bash\n\
exec s6-setuidgid abc /app/readarr/bin/Readarr -nobrowser -data=/config\n\
' > /etc/s6-overlay/s6-rc.d/svc-readarr/run && \
    chmod +x /etc/s6-overlay/s6-rc.d/svc-readarr/run && \
    echo "longrun" > /etc/s6-overlay/s6-rc.d/svc-readarr/type && \
    touch /etc/s6-overlay/s6-rc.d/svc-readarr/dependencies.d/init-readarr && \
    touch /etc/s6-overlay/s6-rc.d/user/contents.d/init-readarr && \
    touch /etc/s6-overlay/s6-rc.d/user/contents.d/svc-readarr

EXPOSE 8787

VOLUME ["/config"]

HEALTHCHECK --interval=30s --timeout=10s --retries=3 \
    CMD curl -f http://localhost:8787/ping || exit 1

ENTRYPOINT ["/init"]
