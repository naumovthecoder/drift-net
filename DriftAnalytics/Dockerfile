FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

# Устанавливаем curl для проверки здоровья
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Копируем файлы проекта
COPY DriftAnalytics/DriftAnalytics.csproj DriftAnalytics/
RUN dotnet restore DriftAnalytics/DriftAnalytics.csproj

# Копируем исходный код
COPY DriftAnalytics/ DriftAnalytics/

# Собираем приложение
RUN dotnet build DriftAnalytics/DriftAnalytics.csproj -c Release -o /app/build

# Публикуем приложение
FROM build AS publish
RUN dotnet publish DriftAnalytics/DriftAnalytics.csproj -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Монтируем Docker socket для доступа к API
VOLUME ["/var/run/docker.sock"]

ENTRYPOINT ["dotnet", "DriftAnalytics.dll"] 