FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY apps/api/TravelControl.Api.csproj apps/api/
RUN dotnet restore apps/api/TravelControl.Api.csproj
COPY apps/api/ apps/api/
RUN dotnet publish apps/api/TravelControl.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
RUN groupadd --system travel && useradd --system --gid travel --home /app travel && mkdir -p /app/storage && chown -R travel:travel /app
COPY --from=build --chown=travel:travel /app/publish .
USER travel
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "TravelControl.Api.dll"]

