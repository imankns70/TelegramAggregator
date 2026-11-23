FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /source
COPY TelegramAggregator.sln ./
COPY src/TelegramAggregator.WebAPI/TelegramAggregator.WebAPI.csproj src/TelegramAggregator.WebAPI/
RUN dotnet restore src/TelegramAggregator.WebAPI/TelegramAggregator.WebAPI.csproj
COPY src/ src/
RUN dotnet publish src/TelegramAggregator.WebAPI/TelegramAggregator.WebAPI.csproj \
    -c Release --no-restore -o /publish

# Keep this tag in sync with Microsoft.Playwright in the project file.
FROM mcr.microsoft.com/playwright/dotnet:v1.60.0-noble
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production
COPY --from=build /publish/ ./
RUN find /app/.playwright/node -type f -name node -exec chmod a+x {} +
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "TelegramAggregator.WebAPI.dll"]
