FROM mcr.microsoft.com/dotnet/aspnet:8.0-noble AS base
WORKDIR /app

USER root
RUN groupadd -r appgroup && useradd -r -g appgroup -u 10001 appuser && chown appuser:appgroup /app


FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["APICommande/APICommande.csproj", "APICommande/"]
RUN dotnet restore "APICommande/APICommande.csproj"

COPY . .
RUN dotnet publish "APICommande/APICommande.csproj" -c Release -o /app

FROM base AS final
WORKDIR /app

COPY --from=build /app .
USER appuser

EXPOSE 8080
ENTRYPOINT ["dotnet", "APICommande.dll"]