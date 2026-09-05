# syntax=docker/dockerfile:1

# ---- Stage 1: build the Tailwind stylesheet --------------------------------
FROM node:22-alpine AS css
WORKDIR /web
COPY src/QaTracker.Web/package.json src/QaTracker.Web/package-lock.json* ./
RUN npm install --no-audit --no-fund
COPY src/QaTracker.Web/tailwind.config.js ./
COPY src/QaTracker.Web/Styles ./Styles
COPY src/QaTracker.Web/Components ./Components
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
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080
USER $APP_UID
ENTRYPOINT ["dotnet", "QaTracker.Web.dll"]
