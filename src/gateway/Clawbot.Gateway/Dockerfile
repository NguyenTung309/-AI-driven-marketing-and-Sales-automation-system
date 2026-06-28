FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5050
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Directory.Build.props", "Directory.Packages.props", "global.json", "./"]
COPY ["src/gateway/Clawbot.Gateway/Clawbot.Gateway.csproj", "src/gateway/Clawbot.Gateway/"]
RUN dotnet restore "src/gateway/Clawbot.Gateway/Clawbot.Gateway.csproj"
COPY . .
WORKDIR "/src/src/gateway/Clawbot.Gateway"
RUN dotnet build -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Clawbot.Gateway.dll"]
