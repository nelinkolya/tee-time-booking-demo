# UI built by Node, API published by the SDK; the final image holds only the runtime and the output.

FROM node:25-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY TeeTimeBooking.Api/TeeTimeBooking.Api.csproj TeeTimeBooking.Api/
RUN dotnet restore TeeTimeBooking.Api/TeeTimeBooking.Api.csproj
COPY TeeTimeBooking.Api/ TeeTimeBooking.Api/
# Declared here, after restore, so a new commit does not invalidate the restore layer.
ARG GIT_SHA=""
ARG BUILD_TIMESTAMP=""
RUN dotnet publish TeeTimeBooking.Api/TeeTimeBooking.Api.csproj -c Release -o /app --no-restore -p:UseAppHost=false \
    -p:SourceRevisionId="$GIT_SHA" -p:BuildTimestamp="$BUILD_TIMESTAMP"

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /web/dist ./wwwroot
# The connection string comes from the environment (ConnectionStrings__TeeTimes), never from the image.
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "TeeTimeBooking.Api.dll"]
