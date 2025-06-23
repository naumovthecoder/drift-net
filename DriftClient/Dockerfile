FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files
COPY DriftClient/DriftClient.csproj DriftClient/
RUN dotnet restore DriftClient/DriftClient.csproj

# Copy source code
COPY DriftClient/ DriftClient/

# Build the application
RUN dotnet build DriftClient/DriftClient.csproj -c Release -o /app/build

# Publish the application
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/build .

# Set the entry point
ENTRYPOINT ["dotnet", "DriftClient.dll"] 