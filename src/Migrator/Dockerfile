# =============================================================================
#  StockPortfolio.Migrator
#
#  Console app: resolves every module's DbContext, calls MigrateAsync() in
#  order as the `migrator` role, then exits. One image, one entrypoint, used
#  identically by the compose `migrations` service and the ACA `job-migrate`.
#  The API itself must never call Migrate() at startup.
#
#  Build context is the REPOSITORY ROOT:
#      docker build -f src/Migrator/Dockerfile .
# =============================================================================

# Pinned to the exact global.json version - rollForward is `latestFeature`,
# which only rolls FORWARD.
FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
WORKDIR /src

# Build infrastructure FIRST: with Central Package Management a .csproj carries
# no versions, so restoring without Directory.Packages.props fails with NU1008 -
# and never fails that way on a developer machine.
COPY global.json Directory.Build.props Directory.Build.targets Directory.Packages.props ./

# Project files only, so restore stays cached.
COPY src/Shared.Kernel/StockPortfolio.Shared.Kernel.csproj                 src/Shared.Kernel/
COPY src/Shared.Api/StockPortfolio.Shared.Api.csproj     src/Shared.Api/
COPY src/Migrator/StockPortfolio.Migrator.csproj                           src/Migrator/

COPY src/Modules/Alerts/StockPortfolio.Modules.Alerts.Contracts/StockPortfolio.Modules.Alerts.Contracts.csproj                   src/Modules/Alerts/StockPortfolio.Modules.Alerts.Contracts/
COPY src/Modules/Alerts/StockPortfolio.Modules.Alerts.Domain/StockPortfolio.Modules.Alerts.Domain.csproj                         src/Modules/Alerts/StockPortfolio.Modules.Alerts.Domain/
COPY src/Modules/Alerts/StockPortfolio.Modules.Alerts.Application/StockPortfolio.Modules.Alerts.Application.csproj               src/Modules/Alerts/StockPortfolio.Modules.Alerts.Application/
COPY src/Modules/Alerts/StockPortfolio.Modules.Alerts.Infrastructure/StockPortfolio.Modules.Alerts.Infrastructure.csproj         src/Modules/Alerts/StockPortfolio.Modules.Alerts.Infrastructure/
COPY src/Modules/Alerts/StockPortfolio.Modules.Alerts.Api/StockPortfolio.Modules.Alerts.Api.csproj             src/Modules/Alerts/StockPortfolio.Modules.Alerts.Api/

COPY src/Modules/Identity/StockPortfolio.Modules.Identity.Contracts/StockPortfolio.Modules.Identity.Contracts.csproj             src/Modules/Identity/StockPortfolio.Modules.Identity.Contracts/
COPY src/Modules/Identity/StockPortfolio.Modules.Identity.Domain/StockPortfolio.Modules.Identity.Domain.csproj                   src/Modules/Identity/StockPortfolio.Modules.Identity.Domain/
COPY src/Modules/Identity/StockPortfolio.Modules.Identity.Application/StockPortfolio.Modules.Identity.Application.csproj         src/Modules/Identity/StockPortfolio.Modules.Identity.Application/
COPY src/Modules/Identity/StockPortfolio.Modules.Identity.Infrastructure/StockPortfolio.Modules.Identity.Infrastructure.csproj   src/Modules/Identity/StockPortfolio.Modules.Identity.Infrastructure/
COPY src/Modules/Identity/StockPortfolio.Modules.Identity.Api/StockPortfolio.Modules.Identity.Api.csproj       src/Modules/Identity/StockPortfolio.Modules.Identity.Api/

COPY src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Contracts/StockPortfolio.Modules.Portfolio.Contracts.csproj          src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Contracts/
COPY src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Domain/StockPortfolio.Modules.Portfolio.Domain.csproj                src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Domain/
COPY src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Application/StockPortfolio.Modules.Portfolio.Application.csproj      src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Application/
COPY src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Infrastructure/StockPortfolio.Modules.Portfolio.Infrastructure.csproj src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Infrastructure/
COPY src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Api/StockPortfolio.Modules.Portfolio.Api.csproj    src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Api/

COPY src/Modules/MarketData/StockPortfolio.Modules.MarketData.Contracts/StockPortfolio.Modules.MarketData.Contracts.csproj       src/Modules/MarketData/StockPortfolio.Modules.MarketData.Contracts/
COPY src/Modules/MarketData/StockPortfolio.Modules.MarketData.Domain/StockPortfolio.Modules.MarketData.Domain.csproj             src/Modules/MarketData/StockPortfolio.Modules.MarketData.Domain/
COPY src/Modules/MarketData/StockPortfolio.Modules.MarketData.Application/StockPortfolio.Modules.MarketData.Application.csproj   src/Modules/MarketData/StockPortfolio.Modules.MarketData.Application/
COPY src/Modules/MarketData/StockPortfolio.Modules.MarketData.Infrastructure/StockPortfolio.Modules.MarketData.Infrastructure.csproj src/Modules/MarketData/StockPortfolio.Modules.MarketData.Infrastructure/
COPY src/Modules/MarketData/StockPortfolio.Modules.MarketData.Api/StockPortfolio.Modules.MarketData.Api.csproj src/Modules/MarketData/StockPortfolio.Modules.MarketData.Api/

RUN dotnet restore src/Migrator/StockPortfolio.Migrator.csproj

COPY src/Shared.Kernel/       src/Shared.Kernel/
COPY src/Shared.Api/ src/Shared.Api/
COPY src/Modules/            src/Modules/
COPY src/Migrator/           src/Migrator/

# `-o /app` is explicit: Directory.Build.props sets <ArtifactsPath>, so the
# default publish output is not bin/Release/net10.0/publish.
RUN dotnet publish src/Migrator/StockPortfolio.Migrator.csproj \
        --configuration Release \
        --no-restore \
        --output /app


# No ASP.NET Core here - this process never serves HTTP.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app .

# The base image CREATES `app` but only the -chiseled variants SET it.
USER app

ENTRYPOINT ["dotnet", "StockPortfolio.Migrator.dll"]
