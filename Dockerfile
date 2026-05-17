FROM mcr.microsoft.com/dotnet/sdk:9.0.311 AS build
WORKDIR /src

# NuGet sources for Docker build.
# Put NuGet.Config next to docker-compose.yml / VarPrice.sln.
# Put Telerik .nupkg files into ./local-nuget/telerik/.
COPY global.json ./
COPY NuGet.Config ./
COPY local-nuget/telerik/ local-nuget/telerik/

COPY VarPrice.Web/VarPrice.Web.csproj VarPrice.Web/
COPY VarPrice.Domain/VarPrice.Domain.csproj VarPrice.Domain/
COPY VarPrice.Application/VarPrice.Application.csproj VarPrice.Application/
COPY VarPrice.Infrastructure/VarPrice.Infrastructure.csproj VarPrice.Infrastructure/

RUN dotnet restore VarPrice.Web/VarPrice.Web.csproj --configfile NuGet.Config

COPY . .

RUN dotnet publish VarPrice.Web/VarPrice.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "VarPrice.Web.dll"]
