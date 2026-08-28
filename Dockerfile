FROM node:24-bookworm-slim AS web-build
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /src
COPY TravelControl.slnx ./
COPY src/TravelControl.Domain/TravelControl.Domain.csproj src/TravelControl.Domain/
COPY src/TravelControl.Application/TravelControl.Application.csproj src/TravelControl.Application/
COPY src/TravelControl.Infrastructure/TravelControl.Infrastructure.csproj src/TravelControl.Infrastructure/
COPY src/TravelControl.Api/TravelControl.Api.csproj src/TravelControl.Api/
RUN dotnet restore src/TravelControl.Api/TravelControl.Api.csproj
COPY src/ src/
RUN dotnet publish src/TravelControl.Api/TravelControl.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false
COPY --from=web-build /src/web/dist /app/publish/wwwroot

FROM mcr.microsoft.com/dotnet/aspnet:10.0-bookworm-slim AS final
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /var/lib/travel-control/data /var/lib/travel-control/attachments /var/lib/travel-control/keys /var/lib/travel-control/private \
    && chown -R app:app /var/lib/travel-control
WORKDIR /app
COPY --from=api-build --chown=app:app /app/publish .
USER app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "TravelControl.Api.dll"]
