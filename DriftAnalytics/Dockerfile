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

# Финальный образ
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Устанавливаем Docker CLI для доступа к логам контейнеров
RUN apt-get update && apt-get install -y docker.io && rm -rf /var/lib/apt/lists/*

ENTRYPOINT ["dotnet", "DriftAnalytics.dll"] 