FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY VarPrice.Web/VarPrice.Web.csproj VarPrice.Web/
COPY VarPrice.Domain/VarPrice.Domain.csproj VarPrice.Domain/
COPY VarPrice.Application/VarPrice.Application.csproj VarPrice.Application/
COPY VarPrice.Infrastructure/VarPrice.Infrastructure.csproj VarPrice.Infrastructure/

RUN dotnet restore VarPrice.Web/VarPrice.Web.csproj

COPY . .

RUN ls -la
RUN ls -la VarPrice.Web
RUN ls -la VarPrice.Domain
RUN ls -la VarPrice.Application
RUN ls -la VarPrice.Infrastructure

RUN dotnet publish VarPrice.Web/VarPrice.Web.csproj -c Release -o /app/publish -v diag

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "VarPrice.Web.dll"]
