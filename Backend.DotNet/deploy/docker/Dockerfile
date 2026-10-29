# Stage 1: Restore & Publish with ReadyToRun
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props nuget.config Rge.Accounting.slnx ./
COPY src/ ./src/

RUN dotnet publish src/Host/Rge.Accounting.Host/Rge.Accounting.Host.csproj \
    -c Release \
    -o /app/publish \
    /p:PublishReadyToRun=true

# Stage 2: Hardened Non-Root Runtime Image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS="http://+:8080" \
    DOTNET_TieredPGO="1" \
    DOTNET_ReadyToRun="1"

USER $APP_UID
COPY --from=build /app/publish .

EXPOSE 8080
ENTRYPOINT ["dotnet", "Rge.Accounting.Host.dll"]

