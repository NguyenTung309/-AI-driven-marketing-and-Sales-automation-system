FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 5050

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Clawbot.Gateway/Clawbot.Gateway.csproj", "src/Clawbot.Gateway/"]
RUN dotnet restore "src/Clawbot.Gateway/Clawbot.Gateway.csproj"
COPY . .
WORKDIR "/src/src/Clawbot.Gateway"
RUN dotnet build -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Clawbot.Gateway.dll"]
