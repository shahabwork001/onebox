FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN --mount=type=cache,id=centralchat-nuget-v2,target=/root/.nuget/packages dotnet restore CentralChat.sln --configfile NuGet.Config
RUN --mount=type=cache,id=centralchat-nuget-v2,target=/root/.nuget/packages dotnet publish src/CentralChat.API/CentralChat.API.csproj -c Release -o /app --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "CentralChat.API.dll"]
