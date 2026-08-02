FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["LgymApi.Api/LgymApi.Api.csproj", "LgymApi.Api/"]
COPY ["LgymApi.BackgroundWorker/LgymApi.BackgroundWorker.csproj", "LgymApi.BackgroundWorker/"]
COPY ["LgymApi.BackgroundWorker.Common/LgymApi.BackgroundWorker.Common.csproj", "LgymApi.BackgroundWorker.Common/"]
COPY ["LgymApi.Application/LgymApi.Application.csproj", "LgymApi.Application/"]
COPY ["LgymApi.Infrastructure/LgymApi.Infrastructure.csproj", "LgymApi.Infrastructure/"]
COPY ["LgymApi.Domain/LgymApi.Domain.csproj", "LgymApi.Domain/"]
COPY ["LgymApi.Identity/LgymApi.Identity.csproj", "LgymApi.Identity/"]
COPY ["LgymApi.Notifications/LgymApi.Notifications.csproj", "LgymApi.Notifications/"]
COPY ["LgymApi.Platform/LgymApi.Platform.csproj", "LgymApi.Platform/"]
COPY ["LgymApi.Resources/LgymApi.Resources.csproj", "LgymApi.Resources/"]
COPY ["LgymApi.Resources.Generator/LgymApi.Resources.Generator.csproj", "LgymApi.Resources.Generator/"]
COPY ["LgymApi.TrainingPlanning/LgymApi.TrainingPlanning.csproj", "LgymApi.TrainingPlanning/"]

RUN dotnet restore "LgymApi.Api/LgymApi.Api.csproj"

COPY ["LgymApi.Api/", "LgymApi.Api/"]
COPY ["LgymApi.BackgroundWorker/", "LgymApi.BackgroundWorker/"]
COPY ["LgymApi.BackgroundWorker.Common/", "LgymApi.BackgroundWorker.Common/"]
COPY ["LgymApi.Application/", "LgymApi.Application/"]
COPY ["LgymApi.Infrastructure/", "LgymApi.Infrastructure/"]
COPY ["LgymApi.Domain/", "LgymApi.Domain/"]
COPY ["LgymApi.Identity/", "LgymApi.Identity/"]
COPY ["LgymApi.Notifications/", "LgymApi.Notifications/"]
COPY ["LgymApi.Platform/", "LgymApi.Platform/"]
COPY ["LgymApi.Resources/", "LgymApi.Resources/"]
COPY ["LgymApi.Resources.Generator/", "LgymApi.Resources.Generator/"]
COPY ["LgymApi.TrainingPlanning/", "LgymApi.TrainingPlanning/"]

RUN find . \( -type d -name bin -o -type d -name obj \) -prune -exec rm -rf {} +

RUN dotnet publish "LgymApi.Api/LgymApi.Api.csproj" --configuration $BUILD_CONFIGURATION --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 CMD ["curl", "--fail", "--silent", "--show-error", "http://127.0.0.1:8080/health/live"]

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "LgymApi.Api.dll"]
