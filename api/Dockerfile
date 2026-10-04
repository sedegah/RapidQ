FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["shared/QueueManagement.Shared.csproj", "shared/"]
COPY ["client/QueueManagement.Client.csproj", "client/"]
COPY ["api/QueueManagement.Api.csproj", "api/"]
RUN dotnet restore "api/QueueManagement.Api.csproj"

COPY shared/ shared/
COPY client/ client/
COPY api/ api/

WORKDIR "/src/api"
RUN dotnet publish "QueueManagement.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production
ENV RAPIDQ_DB_PATH=/var/data/QueueManagement.db

COPY --from=build /app/publish .

RUN mkdir -p /var/data

ENTRYPOINT ["dotnet", "QueueManagement.Api.dll"]
