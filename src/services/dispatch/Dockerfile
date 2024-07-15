# Build from the repository root: docker build -f src/services/dispatch/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:8.0.204-bookworm-slim AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY shared/ shared/
COPY src/services/dispatch/ParcelTrace.Dispatch/ParcelTrace.Dispatch.csproj src/services/dispatch/ParcelTrace.Dispatch/
RUN dotnet restore src/services/dispatch/ParcelTrace.Dispatch/ParcelTrace.Dispatch.csproj
COPY src/services/dispatch/ParcelTrace.Dispatch/ src/services/dispatch/ParcelTrace.Dispatch/
RUN dotnet publish src/services/dispatch/ParcelTrace.Dispatch/ParcelTrace.Dispatch.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0.4-bookworm-slim AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0
EXPOSE 8080
COPY --from=build /app .
USER app
ENTRYPOINT ["dotnet", "ParcelTrace.Dispatch.dll"]
