FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY PreviewDeploy.slnx ./
COPY src/PreviewDeploy.Server/PreviewDeploy.Server.csproj src/PreviewDeploy.Server/
COPY tests/PreviewDeploy.Server.Tests/PreviewDeploy.Server.Tests.csproj tests/PreviewDeploy.Server.Tests/
RUN dotnet restore

COPY . .
RUN dotnet publish src/PreviewDeploy.Server/PreviewDeploy.Server.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl git \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/data \
    && chown $APP_UID /app/data

COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DATA_DIR=/app/data

USER $APP_UID
EXPOSE 8080 443

ENTRYPOINT ["dotnet", "PreviewDeploy.Server.dll"]
