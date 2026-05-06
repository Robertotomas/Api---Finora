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

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:${PORT:-10000}
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 10000
ENTRYPOINT ["dotnet", "Finora.Api.dll"]
