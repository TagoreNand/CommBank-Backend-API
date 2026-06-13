# syntax=docker/dockerfile:1

# ---------- build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore against project files only, so the (slow) restore layer is cached until dependencies change.
COPY CommBank-Server-main/Server.sln CommBank-Server-main/
COPY CommBank-Server-main/CommBank-Server/CommBank.csproj CommBank-Server-main/CommBank-Server/
COPY CommBank-Server-main/CommBank.Tests/CommBank.Tests.csproj CommBank-Server-main/CommBank.Tests/
RUN dotnet restore CommBank-Server-main/CommBank-Server/CommBank.csproj

# Copy the rest of the sources and publish a framework-dependent build.
COPY CommBank-Server-main/ CommBank-Server-main/
RUN dotnet publish CommBank-Server-main/CommBank-Server/CommBank.csproj \
    -c Release -o /app/publish /p:UseAppHost=false

# ---------- runtime stage ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# curl is only needed for the HEALTHCHECK. The .NET 8 image already ships a non-root 'app' user.
USER root
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
USER app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=25s --retries=3 \
  CMD curl -fsS http://localhost:8080/health/live || exit 1

# AssemblyName in CommBank.csproj is "CommBank-Server".
ENTRYPOINT ["dotnet", "CommBank-Server.dll"]
