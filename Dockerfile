FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY *.sln ./
COPY PokerTournament.Api/*.csproj ./PokerTournament.Api/
COPY PokerTournament.Application/*.csproj ./PokerTournament.Application/
COPY PokerTournament.Domain/*.csproj ./PokerTournament.Domain/
COPY PokerTournament.Infrastructure/*.csproj ./PokerTournament.Infrastructure/
RUN dotnet restore PokerTournament.Api/PokerTournament.Api.csproj

COPY . .
RUN dotnet publish PokerTournament.Api/PokerTournament.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

ENTRYPOINT ["dotnet", "PokerTournament.Api.dll"]
