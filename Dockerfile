# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/RedeStore.Domain/RedeStore.Domain.csproj src/RedeStore.Domain/
COPY src/RedeStore.Application/RedeStore.Application.csproj src/RedeStore.Application/
COPY src/RedeStore.Infrastructure/RedeStore.Infrastructure.csproj src/RedeStore.Infrastructure/
COPY src/RedeStore.Api/RedeStore.Api.csproj src/RedeStore.Api/
RUN dotnet restore src/RedeStore.Api/RedeStore.Api.csproj

COPY src/ src/
RUN dotnet publish src/RedeStore.Api/RedeStore.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "RedeStore.Api.dll"]
