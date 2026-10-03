# Stage 1: Build Blazor WebAssembly Client
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-client
WORKDIR /src
COPY ["shared/QueueManagement.Shared.csproj", "shared/"]
COPY ["client/QueueManagement.Client.csproj", "client/"]
RUN dotnet restore "client/QueueManagement.Client.csproj"
COPY shared/ shared/
COPY client/ client/
WORKDIR "/src/client"
RUN dotnet publish "QueueManagement.Client.csproj" -c Release -o /app/client-publish

# Stage 2: Build ASP.NET Core API Backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-api
WORKDIR /src
COPY ["shared/QueueManagement.Shared.csproj", "shared/"]
COPY ["api/QueueManagement.Api.csproj", "api/"]
RUN dotnet restore "api/QueueManagement.Api.csproj"
COPY shared/ shared/
COPY api/ api/
WORKDIR "/src/api"
RUN dotnet publish "QueueManagement.Api.csproj" -c Release -o /app/api-publish /p:UseAppHost=false

# Stage 3: Final Runtime Image (Unified Full-Stack Container)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Copy published API
COPY --from=build-api /app/api-publish .

# Copy published Blazor Client static assets into API's wwwroot for unified hosting
COPY --from=build-client /app/client-publish/wwwroot ./wwwroot

# Ensure storage directory exists for SQLite
RUN mkdir -p /app/storage

ENTRYPOINT ["dotnet", "QueueManagement.Api.dll"]
