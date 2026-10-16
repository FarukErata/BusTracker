FROM node:22-alpine AS web
WORKDIR /src/client
COPY client/package.json client/package-lock.json ./
RUN npm ci
COPY client/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY Directory.Build.props ./
COPY src/BusTracker.Api/BusTracker.Api.csproj src/BusTracker.Api/
RUN dotnet restore src/BusTracker.Api/BusTracker.Api.csproj
COPY src/BusTracker.Api/ src/BusTracker.Api/
RUN dotnet publish src/BusTracker.Api/BusTracker.Api.csproj -c Release -o /out --no-restore \
    && mkdir -p /data

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra
WORKDIR /app
COPY --from=api /out ./
COPY --from=web /src/src/BusTracker.Api/wwwroot ./wwwroot
COPY --from=api --chown=1654:1654 /data /data
ARG COMMIT=unknown
ENV BUSTRACKER_COMMIT=$COMMIT \
    ASPNETCORE_ENVIRONMENT=Production \
    Urls=http://+:8080 \
    BusTracker__DatabasePath=/data/bustracker.db
LABEL org.opencontainers.image.source=https://github.com/FarukErata/BusTracker
USER 1654
EXPOSE 8080
VOLUME /data
ENTRYPOINT ["dotnet", "BusTracker.Api.dll"]
