# Stage 1: Build & Publish Unified Full-Stack Application
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files for restore
COPY ["shared/QueueManagement.Shared.csproj", "shared/"]
COPY ["client/QueueManagement.Client.csproj", "client/"]
COPY ["api/QueueManagement.Api.csproj", "api/"]

RUN dotnet restore "api/QueueManagement.Api.csproj"

# Copy all source files
COPY shared/ shared/
COPY client/ client/
COPY api/ api/

WORKDIR "/src/api"
RUN dotnet publish "QueueManagement.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Final Production Runtime Image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
# ASPNETCORE_URLS is intentionally omitted here so the PORT env var
# injected by Render (or similar platforms) takes effect via Program.cs.
# A default of 8080 is only used if PORT is also unset.
ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish .

# Ensure storage directory exists for SQLite database
RUN mkdir -p /app/storage

ENTRYPOINT ["dotnet", "QueueManagement.Api.dll"]
