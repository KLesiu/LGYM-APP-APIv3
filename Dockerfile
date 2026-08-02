# syntax=docker/dockerfile:1

ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY LgymApi.Api/*.csproj LgymApi.Api/
COPY LgymApi.Application/*.csproj LgymApi.Application/
COPY LgymApi.BackgroundWorker/*.csproj LgymApi.BackgroundWorker/
COPY LgymApi.BackgroundWorker.Common/*.csproj LgymApi.BackgroundWorker.Common/
COPY LgymApi.Domain/*.csproj LgymApi.Domain/
COPY LgymApi.Identity/*.csproj LgymApi.Identity/
COPY LgymApi.Infrastructure/*.csproj LgymApi.Infrastructure/
COPY LgymApi.Notifications/*.csproj LgymApi.Notifications/
COPY LgymApi.Platform/*.csproj LgymApi.Platform/
COPY LgymApi.Resources/*.csproj LgymApi.Resources/
COPY LgymApi.Resources.Generator/*.csproj LgymApi.Resources.Generator/
COPY LgymApi.TrainingPlanning/*.csproj LgymApi.TrainingPlanning/

RUN dotnet restore "LgymApi.Api/LgymApi.Api.csproj"

COPY . .

RUN dotnet publish "LgymApi.Api/LgymApi.Api.csproj" \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0

RUN apt-get update \
    && apt-get install --yes --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

RUN mkdir -p /run/config

COPY --from=build /app/publish/ ./

USER $APP_UID
EXPOSE 8080

ENTRYPOINT ["dotnet", "LgymApi.Api.dll"]
