FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY api/Skoleoverblikket.Api/Skoleoverblikket.Api.csproj api/Skoleoverblikket.Api/
COPY infrastructure/aspire/Skoleoverblikket.ServiceDefaults/Skoleoverblikket.ServiceDefaults.csproj infrastructure/aspire/Skoleoverblikket.ServiceDefaults/
RUN dotnet restore api/Skoleoverblikket.Api/Skoleoverblikket.Api.csproj

COPY api/ api/
COPY infrastructure/aspire/Skoleoverblikket.ServiceDefaults/ infrastructure/aspire/Skoleoverblikket.ServiceDefaults/
RUN dotnet publish api/Skoleoverblikket.Api/Skoleoverblikket.Api.csproj \
    --no-restore \
    --configuration Release \
    --output /app/publish \
    -p:CI=true # Set CI=true to disable OpenAPI generation during publish, since it modifies source files

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
# Commit sha, set by CI. Read as App:Version (X-App-Version on /api/health, elmah.io message version).
ARG APP_VERSION=dev
ENV App__Version=$APP_VERSION
ENTRYPOINT ["dotnet", "Skoleoverblikket.Api.dll"]
