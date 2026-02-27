FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/ ./src/

RUN dotnet restore src/ElRucio.Host/ElRucio.Host.csproj
RUN dotnet publish src/ElRucio.Host/ElRucio.Host.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg nodejs npm \
    && npm install -g @github/copilot \
    && rm -rf /var/lib/apt/lists/*

RUN groupadd --system elrucio \
    && useradd --system --gid elrucio --home /var/lib/elrucio --shell /usr/sbin/nologin elrucio \
    && mkdir -p /var/lib/elrucio/data \
    && chown -R elrucio:elrucio /var/lib/elrucio

ENV DOTNET_ENVIRONMENT=Production
ENV ElRucio__DataDir=/var/lib/elrucio/data
ENV Copilot__CliPath=/usr/local/bin/copilot

COPY --from=build /app/publish/ /app/

VOLUME ["/var/lib/elrucio"]

USER elrucio

ENTRYPOINT ["dotnet", "ElRucio.Host.dll"]