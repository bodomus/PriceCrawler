dotnet publish .\src\PriceCrawler.Web\PriceCrawler.Web.csproj -c Release -o .\artifacts\publish\web
dotnet publish .\src\PriceCrawler.Worker\PriceCrawler.Worker.csproj -c Release -o .\artifacts\publish\crawler

# 1. Очистить старый publish
Remove-Item ".\artifacts\publish" -Recurse -Force -ErrorAction SilentlyContinue

# 2. Создать каталоги
New-Item ".\artifacts\publish\web" -ItemType Directory -Force | Out-Null
New-Item ".\artifacts\publish\crawler" -ItemType Directory -Force | Out-Null

# 3. Publish WEB
dotnet publish `
    ".\src\PriceCrawler.Web\PriceCrawler.Web.csproj" `
    -c Release `
    -o ".\artifacts\publish\web"

if ($LASTEXITCODE -ne 0) {
    throw "WEB publish failed."
}

# 4. Publish Crawler
dotnet publish `
    ".\src\PriceCrawler.Worker\PriceCrawler.Worker.csproj" `
    -c Release `
    -o ".\artifacts\publish\crawler"

if ($LASTEXITCODE -ne 0) {
    throw "Crawler publish failed."
}