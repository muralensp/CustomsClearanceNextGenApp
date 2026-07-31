# Day 13 - containerizes the placeholder Api host. Multi-stage build:
# SDK image compiles, ASP.NET runtime image runs (smaller final image,
# no SDK/build tools shipped to the "dev environment").
#
# Build context is the repository root (see docker-compose.yml), since the
# Api project references sibling projects (e.g. Platform.Common) that live
# outside src/Api/ - per ADR-0003's bounded-context-first structure.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY CustomsClearance.sln Directory.Build.props ./
COPY src/Api/CustomsClearance.Api.csproj src/Api/
COPY src/Platform/Common/CustomsClearance.Platform.Common.csproj src/Platform/Common/

RUN dotnet restore src/Api/CustomsClearance.Api.csproj

COPY src/Api/ src/Api/
COPY src/Platform/Common/ src/Platform/Common/

RUN dotnet publish src/Api/CustomsClearance.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Development

ENTRYPOINT ["dotnet", "CustomsClearance.Api.dll"]
