FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY WmhLms.Api/WmhLms.Api.csproj WmhLms.Api/
COPY WmhLms.Core/WmhLms.Core.csproj WmhLms.Core/
COPY WmhLms.Data/WmhLms.Data.csproj WmhLms.Data/
RUN dotnet restore WmhLms.Api/WmhLms.Api.csproj
COPY WmhLms.Api/ WmhLms.Api/
COPY WmhLms.Core/ WmhLms.Core/
COPY WmhLms.Data/ WmhLms.Data/
RUN dotnet publish WmhLms.Api/WmhLms.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
RUN adduser --system --uid 1001 --group wmh
USER wmh
COPY --from=build --chown=wmh:wmh /app .
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    DOTNET_gcServer=0
EXPOSE 8080
ENTRYPOINT ["dotnet", "WmhLms.Api.dll"]
