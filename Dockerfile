FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src

COPY Finora.sln .
COPY src/Finora.Api/Finora.Api.csproj src/Finora.Api/
COPY src/Finora.Application/Finora.Application.csproj src/Finora.Application/
COPY src/Finora.Domain/Finora.Domain.csproj src/Finora.Domain/
COPY src/Finora.Infrastructure/Finora.Infrastructure.csproj src/Finora.Infrastructure/
RUN dotnet restore

COPY . .
RUN dotnet publish src/Finora.Api/Finora.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine
WORKDIR /app
COPY --from=build /app/publish .

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
ENV DOTNET_GCConserveMemory=9
ENV DOTNET_SYSTEM_NET_SOCKETS_DISABLEIPV6=true
ENV ASPNETCORE_ENVIRONMENT=Production

RUN apk add --no-cache icu-libs

EXPOSE 10000
ENTRYPOINT ["dotnet", "Finora.Api.dll"]
