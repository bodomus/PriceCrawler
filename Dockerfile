FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY VarPrice.sln ./
COPY VarPrice.Web/VarPrice.Web.csproj VarPrice.Web/
RUN dotnet restore

COPY VarPrice.Web/ VarPrice.Web/
RUN dotnet publish VarPrice.Web/VarPrice.Web.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "VarPrice.Web.dll"]
