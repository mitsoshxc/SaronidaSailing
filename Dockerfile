FROM mcr.microsoft.com/dotnet/sdk:10.0.403-noble AS build
WORKDIR /src

COPY global.json nuget.config Directory.Build.props Directory.Packages.props ./
COPY src/SaronidaSailing.Core/SaronidaSailing.Core.csproj src/SaronidaSailing.Core/
COPY src/SaronidaSailing.Infrastructure/SaronidaSailing.Infrastructure.csproj src/SaronidaSailing.Infrastructure/
COPY src/SaronidaSailing.Web/SaronidaSailing.Web.csproj src/SaronidaSailing.Web/
RUN dotnet restore src/SaronidaSailing.Web/SaronidaSailing.Web.csproj

COPY src/ src/
RUN dotnet publish src/SaronidaSailing.Web/SaronidaSailing.Web.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14-noble AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "SaronidaSailing.Web.dll"]
