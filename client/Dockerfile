FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["client/QueueManagement.Client.csproj", "client/"]
COPY ["shared/QueueManagement.Shared.csproj", "shared/"]
RUN dotnet restore "client/QueueManagement.Client.csproj"

COPY client/ client/
COPY shared/ shared/
WORKDIR "/src/client"
RUN dotnet publish "QueueManagement.Client.csproj" -c Release -o /app/publish

FROM nginx:alpine AS final
WORKDIR /usr/share/nginx/html
COPY --from=build /app/publish/wwwroot .
COPY client/nginx.conf /etc/nginx/nginx.conf
EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
