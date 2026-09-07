# Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY *.sln ./
COPY WmhLms.Api/*.csproj WmhLms.Api/
COPY WmhLms.Core/*.csproj WmhLms.Core/
COPY WmhLms.Data/*.csproj WmhLms.Data/
COPY WmhLms.Tests/*.csproj WmhLms.Tests/
RUN dotnet restore
COPY . .
RUN dotnet test WmhLms.Tests/WmhLms.Tests.csproj -c Release --nologo
RUN dotnet publish WmhLms.Api/WmhLms.Api.csproj -c Release -o /app --no-restore

# Run
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
# Never run the API as root.
RUN adduser --system --uid 1001 --group wmh \
    && mkdir -p /data && chown -R wmh:wmh /data
USER wmh
COPY --from=build --chown=wmh:wmh /app .

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080

EXPOSE 8080
VOLUME ["/data"]

HEALTHCHECK --interval=30s --timeout=3s --start-period=20s --retries=3 \
    CMD ["/bin/sh", "-c", "wget -qO- http://localhost:8080/health/ready || exit 1"]

ENTRYPOINT ["dotnet", "WmhLms.Api.dll"]
