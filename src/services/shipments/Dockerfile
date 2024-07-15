# Build from the repository root: docker build -f src/services/shipments/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:8.0.204-bookworm-slim AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY shared/ shared/
COPY src/services/shipments/ParcelTrace.Shipments/ParcelTrace.Shipments.csproj src/services/shipments/ParcelTrace.Shipments/
RUN dotnet restore src/services/shipments/ParcelTrace.Shipments/ParcelTrace.Shipments.csproj
COPY src/services/shipments/ParcelTrace.Shipments/ src/services/shipments/ParcelTrace.Shipments/
RUN dotnet publish src/services/shipments/ParcelTrace.Shipments/ParcelTrace.Shipments.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0.4-bookworm-slim AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0
EXPOSE 8080 8081
COPY --from=build /app .
USER app
ENTRYPOINT ["dotnet", "ParcelTrace.Shipments.dll"]
