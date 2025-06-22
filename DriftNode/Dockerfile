# Базовый рантайм-образ .NET (не требует SDK)
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
WORKDIR /app
EXPOSE 5000

# Сборка приложения
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["DriftNode/DriftNode.csproj", "DriftNode/"]
RUN dotnet restore "DriftNode/DriftNode.csproj"
COPY . .
WORKDIR "/src/DriftNode"
RUN dotnet build "DriftNode.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Публикация (оптимизированная сборка)
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "DriftNode.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Финальный минимальный рантайм-слой
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "DriftNode.dll"]
