# Day 22 - containerizes the API Gateway. Same multi-stage pattern as
# src/Api/Dockerfile (Day 13). Build context is the repository root (see
# docker-compose.yml), since the Gateway project references sibling
# Platform projects that live outside src/Gateway/.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY CustomsClearance.sln Directory.Build.props ./
COPY src/Gateway/CustomsClearance.Gateway.csproj src/Gateway/
COPY src/Platform/Common/CustomsClearance.Platform.Common.csproj src/Platform/Common/
COPY src/Platform/Logging/CustomsClearance.Platform.Logging.csproj src/Platform/Logging/
COPY src/Platform/ErrorHandling/CustomsClearance.Platform.ErrorHandling.csproj src/Platform/ErrorHandling/
COPY src/Platform/Security/CustomsClearance.Platform.Security.csproj src/Platform/Security/

RUN dotnet restore src/Gateway/CustomsClearance.Gateway.csproj

COPY src/Gateway/ src/Gateway/
COPY src/Platform/Common/ src/Platform/Common/
COPY src/Platform/Logging/ src/Platform/Logging/
COPY src/Platform/ErrorHandling/ src/Platform/ErrorHandling/
COPY src/Platform/Security/ src/Platform/Security/

RUN dotnet publish src/Gateway/CustomsClearance.Gateway.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Development

ENTRYPOINT ["dotnet", "CustomsClearance.Gateway.dll"]
