# Live vocabulary quiz in one container: the React client is built into wwwroot and served by
# the .NET server. Runs in demo mode (Development: seeded users and quiz sets, Swagger UI).
#   docker compose up --build    -> http://localhost:5080

FROM node:24-alpine AS web
WORKDIR /src
COPY web/package.json web/package-lock.json web/
RUN npm --prefix web ci
COPY web/ web/
# Vite writes to ../src/Quiz.Server/wwwroot (see web/vite.config.ts).
RUN npm --prefix web run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Quiz.Server/Quiz.Server.csproj src/Quiz.Server/
RUN dotnet restore src/Quiz.Server/Quiz.Server.csproj
COPY src/Quiz.Server/ src/Quiz.Server/
COPY --from=web /src/src/Quiz.Server/wwwroot/ src/Quiz.Server/wwwroot/
RUN dotnet publish src/Quiz.Server/Quiz.Server.csproj -c Release -o /app --no-restore -p:DebugType=None

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# The SQLite file lives in App_Data (mount a volume there to keep it); the app runs as the
# image's non-root user, which must own that folder. HOME points there too, so the Data
# Protection keys (they sign the login cookie) survive with the data and logins stay valid
# when the container is recreated. The image listens on 8080 (ASPNETCORE_HTTP_PORTS).
RUN mkdir -p /app/App_Data && chown "$APP_UID" /app/App_Data
USER $APP_UID
ENV ASPNETCORE_ENVIRONMENT=Development \
    HOME=/app/App_Data
EXPOSE 8080
ENTRYPOINT ["dotnet", "Quiz.Server.dll"]
