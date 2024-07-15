# Build from the repository root: docker build -f src/gateway/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:8.0.204-bookworm-slim AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY shared/ shared/
COPY src/gateway/ParcelTrace.Gateway/ParcelTrace.Gateway.csproj src/gateway/ParcelTrace.Gateway/
RUN dotnet restore src/gateway/ParcelTrace.Gateway/ParcelTrace.Gateway.csproj
COPY src/gateway/ParcelTrace.Gateway/ src/gateway/ParcelTrace.Gateway/
RUN dotnet publish src/gateway/ParcelTrace.Gateway/ParcelTrace.Gateway.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0.4-bookworm-slim AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0
EXPOSE 8080
COPY --from=build /app .
USER app
ENTRYPOINT ["dotnet", "ParcelTrace.Gateway.dll"]
