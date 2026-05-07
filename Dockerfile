FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Finora.sln .
COPY src/Finora.Api/Finora.Api.csproj src/Finora.Api/
COPY src/Finora.Application/Finora.Application.csproj src/Finora.Application/
COPY src/Finora.Domain/Finora.Domain.csproj src/Finora.Domain/
COPY src/Finora.Infrastructure/Finora.Infrastructure.csproj src/Finora.Infrastructure/
RUN dotnet restore

COPY . .
RUN dotnet publish src/Finora.Api/Finora.Api.csproj -c Release -o /app/publish

# Install Playwright Chromium browser in build stage
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
RUN pwsh /app/publish/playwright.ps1 install --with-deps chromium

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app

ENV DOTNET_GCConserveMemory=9
ENV DOTNET_SYSTEM_NET_SOCKETS_DISABLEIPV6=true
ENV ASPNETCORE_ENVIRONMENT=Production
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright

# Chromium runtime dependencies
RUN apt-get update && apt-get install -y --no-install-recommends \
    libnss3 libnspr4 libdbus-1-3 libatk1.0-0 libatk-bridge2.0-0 \
    libcups2 libdrm2 libxkbcommon0 libatspi2.0-0 libxcomposite1 \
    libxdamage1 libxfixes3 libxrandr2 libgbm1 libpango-1.0-0 \
    libcairo2 libasound2 libwayland-client0 \
    fonts-liberation fonts-noto-color-emoji \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
COPY --from=build /ms-playwright /ms-playwright

EXPOSE 10000
ENTRYPOINT ["dotnet", "Finora.Api.dll"]
