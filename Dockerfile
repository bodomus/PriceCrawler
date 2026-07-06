FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY PriceCrawler.Web/PriceCrawler.Web.csproj PriceCrawler.Web/
COPY PriceCrawler.Domain/PriceCrawler.Domain.csproj PriceCrawler.Domain/
COPY PriceCrawler.Application/PriceCrawler.Application.csproj PriceCrawler.Application/
COPY PriceCrawler.Infrastructure/PriceCrawler.Infrastructure.csproj PriceCrawler.Infrastructure/

RUN dotnet restore PriceCrawler.Web/PriceCrawler.Web.csproj

COPY . .

RUN ls -la
RUN ls -la PriceCrawler.Web
RUN ls -la PriceCrawler.Domain
RUN ls -la PriceCrawler.Application
RUN ls -la PriceCrawler.Infrastructure

RUN dotnet publish PriceCrawler.Web/PriceCrawler.Web.csproj -c Release -o /app/publish -v diag

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "PriceCrawler.Web.dll"]
