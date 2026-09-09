# syntax=docker/dockerfile:1

# ---- Stage 1: build the Tailwind stylesheet --------------------------------
FROM node:22-alpine AS css
WORKDIR /web
COPY src/QaTracker.Web/package.json src/QaTracker.Web/package-lock.json* ./
RUN npm install --no-audit --no-fund
# Tailwind tree-shakes against the class names it finds in the source (tailwind.config.js
# `content`), which spans the Razor components AND the C# presentation helpers that emit
# badge/chip colour classes. Copy the whole project — cherry-picking folders silently
# purges any class used only in a folder that was left out. node_modules is .dockerignored,
# so the install above is preserved.
COPY src/QaTracker.Web/ ./
RUN npm run css:build

# ---- Stage 2: build & publish the app ------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY QaTracker.slnx ./
COPY src/QaTracker.Web/QaTracker.Web.csproj src/QaTracker.Web/
RUN dotnet restore src/QaTracker.Web/QaTracker.Web.csproj
COPY src/ src/
# Bring in the stylesheet from the css stage and skip the in-build Tailwind step.
COPY --from=css /web/wwwroot/app.css src/QaTracker.Web/wwwroot/app.css
# Not --no-restore: the wwwroot/app.css copy above changes the project's static web
# assets *after* the earlier restore, and publishing against that stale restore silently
# drops the framework's own static web assets (blazor.web.js among them) from the
# published endpoints manifest, breaking all interactivity with a 404 — no build error.
RUN dotnet publish src/QaTracker.Web/QaTracker.Web.csproj \
    -c Release -o /app -p:SkipTailwind=true

# ---- Stage 3: runtime ---------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
# libgssapi-krb5-2: silences an Npgsql diagnostic when loading its Kerberos support.
# curl: used by the container HEALTHCHECK below.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./

# Build identity, fed by CI (see .github/workflows/ci.yml). Kept to the final stage only so
# a new commit doesn't invalidate the cached restore/build/publish layers. Absent when the
# image is built without these args — the app then reports "unknown" (or "Development").
ARG APP_VERSION=
ARG GIT_BRANCH=
ARG GIT_COMMIT=
ARG GIT_COMMIT_SHORT=
ARG BUILD_DATE=
ENV QATRACKER_BUILD_VERSION=$APP_VERSION \
    QATRACKER_BUILD_BRANCH=$GIT_BRANCH \
    QATRACKER_BUILD_COMMIT=$GIT_COMMIT \
    QATRACKER_BUILD_COMMIT_SHORT=$GIT_COMMIT_SHORT \
    QATRACKER_BUILD_DATE=$BUILD_DATE

EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080
# Liveness only (no DB dependency) — a database blip must not restart the container.
HEALTHCHECK --interval=15s --timeout=3s --start-period=40s --retries=3 \
    CMD curl -fsS http://localhost:8080/health/live || exit 1
USER $APP_UID
ENTRYPOINT ["dotnet", "QaTracker.Web.dll"]
