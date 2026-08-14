FROM mcr.microsoft.com/dotnet/aspnet:8.0@sha256:fd7596eaea7ad453fe7ac16724a3c9ae36edcda894ba13743d6a5c83d6a3b36d AS base
WORKDIR /app
EXPOSE 15873
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/logs \
    && chown -R app:app /app

FROM mcr.microsoft.com/dotnet/sdk:8.0.418@sha256:bfb6ed602caa605141700aea7dc7d42574b74b704368e67d683c71a002123808 AS build
WORKDIR /src
COPY . .
RUN dotnet restore "src/gateway/Clawbot.Gateway/Clawbot.Gateway.csproj"
WORKDIR "/src/src/gateway/Clawbot.Gateway"
RUN dotnet publish -c Release --no-restore -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:15873 \
    DOTNET_EnableDiagnostics=0
USER app
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl --fail --silent http://localhost:15873/health/live || exit 1
ENTRYPOINT ["dotnet", "Clawbot.Gateway.dll"]
