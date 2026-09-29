# Compose only; never pushed or deployed. Build from the repository root:
# docker build -f tools/FakeSupplier/Dockerfile -t stockyard-fake-supplier .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from the project files alone, so the layer survives code changes.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY tools/FakeSupplier/FakeSupplier.csproj tools/FakeSupplier/
RUN dotnet restore tools/FakeSupplier/FakeSupplier.csproj

COPY tools/FakeSupplier/ tools/FakeSupplier/
RUN dotnet publish tools/FakeSupplier/FakeSupplier.csproj --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# The base image ships the non-root app user (UID 1654); nothing here writes outside /tmp.
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8090
EXPOSE 8090
ENTRYPOINT ["dotnet", "FakeSupplier.dll"]
