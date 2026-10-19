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
COPY src/Platform/Messaging/CustomsClearance.Platform.Messaging.csproj src/Platform/Messaging/
COPY src/Platform/Observability/CustomsClearance.Platform.Observability.csproj src/Platform/Observability/

RUN dotnet restore src/Gateway/CustomsClearance.Gateway.csproj

COPY src/Gateway/ src/Gateway/
COPY src/Platform/Common/ src/Platform/Common/
COPY src/Platform/Logging/ src/Platform/Logging/
COPY src/Platform/ErrorHandling/ src/Platform/ErrorHandling/
COPY src/Platform/Security/ src/Platform/Security/
COPY src/Platform/Messaging/ src/Platform/Messaging/
COPY src/Platform/Observability/ src/Platform/Observability/

RUN dotnet publish src/Gateway/CustomsClearance.Gateway.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .

EXPOSE 8080
# 8080 is the published edge; 9100 is the internal metrics port (Day 45, ADR-0041) and is never published.
ENV ASPNETCORE_URLS=http://+:8080;http://+:9100
# Day 41 (ADR-0036): the image defaults to Production so a deployed image can never silently run as
# Development (placeholder JWT key). docker-compose.yml sets ASPNETCORE_ENVIRONMENT=Development
# explicitly for the local/CI stack.
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "CustomsClearance.Gateway.dll"]
