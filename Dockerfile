FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /source
COPY TelegramAggregator.sln ./
COPY src/TelegramAggregator.WebAPI/TelegramAggregator.WebAPI.csproj src/TelegramAggregator.WebAPI/
RUN dotnet restore src/TelegramAggregator.WebAPI/TelegramAggregator.WebAPI.csproj
COPY src/ src/
RUN dotnet publish src/TelegramAggregator.WebAPI/TelegramAggregator.WebAPI.csproj \
    -c Release --no-restore -o /publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
COPY --from=build /publish/ ./
RUN dotnet TelegramAggregator.WebAPI.dll --install-browsers \
    && chmod -R a+rX /ms-playwright
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "TelegramAggregator.WebAPI.dll"]
