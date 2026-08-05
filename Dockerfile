FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY WhosHome.Server/WhosHome.Server.csproj WhosHome.Server/
RUN dotnet restore WhosHome.Server/WhosHome.Server.csproj

COPY . .
RUN dotnet publish WhosHome.Server/WhosHome.Server.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# The SQLite file lives here and must be a mounted volume, or the household's history
# disappears every time the image is updated.
VOLUME /data
ENV WhosHome__DatabasePath=/data/whoshome.db

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "WhosHome.Server.dll"]
