# Stage 1: Build & Publish the full stack (API + Blazor WASM client)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore — copy all csproj files first for better layer caching
COPY ["shared/QueueManagement.Shared.csproj", "shared/"]
COPY ["client/QueueManagement.Client.csproj", "client/"]
COPY ["api/QueueManagement.Api.csproj", "api/"]
RUN dotnet restore "api/QueueManagement.Api.csproj"

# Copy all source (api references client, so client MUST be included)
COPY shared/ shared/
COPY client/ client/
COPY api/ api/

WORKDIR "/src/api"
RUN dotnet publish "QueueManagement.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Production runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Do NOT set ASPNETCORE_URLS here — Render injects PORT and Program.cs binds to it.
# Hardcoding this env var would conflict and the app would not listen on Render's port.
ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish .

# Ensure the SQLite storage directory exists
RUN mkdir -p /app/storage

ENTRYPOINT ["dotnet", "QueueManagement.Api.dll"]
