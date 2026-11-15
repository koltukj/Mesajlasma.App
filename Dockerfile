FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY MesajlasmaApp/MesajlasmaApp.csproj MesajlasmaApp/
RUN dotnet restore MesajlasmaApp/MesajlasmaApp.csproj
COPY MesajlasmaApp/ MesajlasmaApp/
RUN dotnet publish MesajlasmaApp/MesajlasmaApp.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DATA_PATH=/data
VOLUME ["/data"]
EXPOSE 8080
ENTRYPOINT ["dotnet", "MesajlasmaApp.dll"]
