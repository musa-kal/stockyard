# Build from the repository root: docker build -f src/Stockyard.Exporter/Dockerfile -t stockyard-exporter .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from the project files alone, so the layer survives code changes.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Stockyard.Domain/Stockyard.Domain.csproj src/Stockyard.Domain/
COPY src/Stockyard.Infrastructure/Stockyard.Infrastructure.csproj src/Stockyard.Infrastructure/
COPY src/Stockyard.Exporter/Stockyard.Exporter.csproj src/Stockyard.Exporter/
RUN dotnet restore src/Stockyard.Exporter/Stockyard.Exporter.csproj

COPY src/Stockyard.Domain/ src/Stockyard.Domain/
COPY src/Stockyard.Infrastructure/ src/Stockyard.Infrastructure/
COPY src/Stockyard.Exporter/ src/Stockyard.Exporter/
RUN dotnet publish src/Stockyard.Exporter/Stockyard.Exporter.csproj --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# The base image ships the non-root app user (UID 1654); nothing here writes outside /tmp.
USER $APP_UID
ENTRYPOINT ["dotnet", "Stockyard.Exporter.dll"]
