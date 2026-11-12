# Build stage: the .NET SDK compiles and publishes the web application.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from the project files only, so that this layer is cached until a dependency changes.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/LivingPaper.Domain/LivingPaper.Domain.csproj src/LivingPaper.Domain/
COPY src/LivingPaper.Application/LivingPaper.Application.csproj src/LivingPaper.Application/
COPY src/LivingPaper.Infrastructure/LivingPaper.Infrastructure.csproj src/LivingPaper.Infrastructure/
COPY src/LivingPaper.Persistence/LivingPaper.Persistence.csproj src/LivingPaper.Persistence/
COPY src/LivingPaper.Web/LivingPaper.Web.csproj src/LivingPaper.Web/
RUN dotnet restore src/LivingPaper.Web/LivingPaper.Web.csproj

COPY src/ src/
COPY examples/ examples/
RUN dotnet publish src/LivingPaper.Web/LivingPaper.Web.csproj --configuration Release --no-restore --output /app

# Run stage: the ASP.NET runtime only, no SDK, no source.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Everything the application stores (books, settings, encrypted keys, prompts) lives in /data: mount a volume there.
# The directory belongs to the unprivileged user of the image, so that a new volume is writable by it.
ENV DATA_DIRECTORY=/data \
    ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production
RUN mkdir /data && chown app:app /data
VOLUME /data

# Never run as root: "app" is the unprivileged user that the Microsoft images provide.
USER app
EXPOSE 8080

# The image has no curl: a TCP connection to the port tells that the server listens.
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080"]

ENTRYPOINT ["dotnet", "LivingPaper.Web.dll"]
