# ── Stage 1: build frontend ───────────────────────────────────────────────────
# The UI is identical on every architecture, so it is built once, natively.
FROM --platform=$BUILDPLATFORM node:20-slim AS frontend-builder
WORKDIR /src

COPY package.json yarn.lock tsconfig.json ./
RUN yarn install --frozen-lockfile --network-timeout 120000

COPY frontend/ ./frontend/
RUN yarn run build --env production

# ── Stage 2: build backend ────────────────────────────────────────────────────
# The SDK also runs natively and cross-publishes for the target architecture, so a
# multi-platform build only emulates the small runtime stage below (apk and pip),
# rather than the whole compile.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:6.0 AS backend-builder
WORKDIR /src

COPY src/ ./src/
COPY Logo/ ./Logo/
# Four-part version: BuildInfo reads Assembly.GetName().Version, which is always
# four-part. The update feed must match exactly, or System.Version comparisons
# treat "11.0.0" (revision -1) as different from "11.0.0.0".
ARG ASSEMBLY_VERSION=11.0.0.0
# Set by buildx to the platform being built (amd64, arm64); .NET calls amd64 "x64".
ARG TARGETARCH
RUN case "$TARGETARCH" in \
      amd64) RID=linux-musl-x64 ;; \
      arm64) RID=linux-musl-arm64 ;; \
      *) echo "unsupported TARGETARCH: '$TARGETARCH'" >&2; exit 1 ;; \
    esac \
    && dotnet msbuild src/Readarr.sln \
      -restore \
      -p:Configuration=Release \
      -p:Platform=Posix \
      -p:RuntimeIdentifiers=$RID \
      -p:EnableAnalyzers=false \
      -p:TreatWarningsAsErrors=false \
      -p:AssemblyVersion=${ASSEMBLY_VERSION} \
      -t:PublishAllRids \
    && mv _output/net6.0/$RID/publish /src/publish

# ── Stage 3: runtime (Alpine) ─────────────────────────────────────────────────
FROM alpine:3.22 AS runtime

# Install system packages + Python for bookinfo
COPY bookinfo/requirements.txt /tmp/bookinfo-requirements.txt
RUN apk add --no-cache \
      bash \
      ca-certificates \
      catatonit \
      coreutils \
      icu-libs \
      libintl \
      nano \
      py3-pip \
      python3 \
      sqlite-libs \
      supervisor \
      tzdata \
    && pip3 install --no-cache-dir --break-system-packages \
         -r /tmp/bookinfo-requirements.txt \
    && rm /tmp/bookinfo-requirements.txt \
    && addgroup -g 1000 readarr \
    && adduser -u 1000 -G readarr -h /config -s /bin/sh -D readarr \
    && mkdir -p /app/bin /app/bookinfo \
    && chown -R readarr:readarr /app

WORKDIR /app

ARG VERSION=11.0.0
ARG VENDOR=ricetim
ARG PackageOwner=ricetim
ARG PackageRepo=readarr
ARG BRANCH=develop

ENV COMPlus_EnableDiagnostics=0
ENV READARR__UPDATE__BRANCH=${BRANCH}

# Copy published backend
COPY --from=backend-builder /src/publish/ /app/bin/
# Copy built frontend
COPY --from=frontend-builder /src/_output/UI/ /app/bin/UI/
# Copy bookinfo Python app
COPY bookinfo/*.py /app/bookinfo/

# Write package_info (matches faustvii convention)
RUN printf "UpdateMethod=docker\nBranch=%s\nPackageVersion=%s\nPackageAuthor=[%s](https://github.com/%s)\nPackageOwner=%s\nPackageRepo=%s\n" \
      "${BRANCH}" "${VERSION}" "${VENDOR}" "${VENDOR}" "${PackageOwner}" "${PackageRepo}" \
    > /app/bin/package_info \
    && rm -rf /app/bin/Readarr.Update \
    && rm -f /app/bin/Readarr.Windows.*

# supervisord config
COPY docker/supervisord.conf /etc/supervisord-readarr.conf

COPY docker/entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

USER readarr
WORKDIR /config
VOLUME ["/config"]

ENTRYPOINT ["/usr/bin/catatonit", "--", "/entrypoint.sh"]
