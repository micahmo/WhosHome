# Node is only ever a build tool here. It compiles the Svelte app to static files and does
# not exist in the final image or at runtime.
FROM node:24-alpine AS web
WORKDIR /web

COPY WhosHome.Web/package.json WhosHome.Web/package-lock.json ./
RUN npm ci

COPY WhosHome.Web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY WhosHome.Server/WhosHome.Server.csproj WhosHome.Server/
RUN dotnet restore WhosHome.Server/WhosHome.Server.csproj

COPY . .
RUN dotnet publish WhosHome.Server/WhosHome.Server.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# ASP.NET Core serves the compiled frontend from wwwroot on the same origin as the API,
# so there is no CORS to configure and the session cookie just works.
COPY --from=web /web/dist ./wwwroot

# The SQLite file and the Data Protection keys live here and must be a mounted volume.
# Losing the keys signs the whole household out; losing the database loses everything.
VOLUME /data
ENV WhosHome__DatabasePath=/data/whoshome.db

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "WhosHome.Server.dll"]
