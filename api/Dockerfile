FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files and restore
COPY ["api/QueueManagement.Api.csproj", "api/"]
COPY ["shared/QueueManagement.Shared.csproj", "shared/"]
RUN dotnet restore "api/QueueManagement.Api.csproj"

# Copy source and publish
COPY api/ api/
COPY shared/ shared/
WORKDIR "/src/api"
RUN dotnet publish "QueueManagement.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "QueueManagement.Api.dll"]
