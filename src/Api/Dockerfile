# =============================================================================
#  StockPortfolio.Api
#
#  Build context is the REPOSITORY ROOT, not this directory:
#      docker build -f src/Api/Dockerfile .
#  Central Package Management and global.json live at the root and restore
#  cannot see them otherwise. docker-compose.yml sets `context: .` accordingly.
# =============================================================================

# The SDK tag is pinned to the exact version in global.json. rollForward is
# `latestFeature`, which only rolls FORWARD - an older SDK image fails the
# build with an unhelpful "compatible SDK not found".
FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
WORKDIR /src

# ---------------------------------------------------------------------------
# 1. Build infrastructure FIRST. With Central Package Management a .csproj has
#    no version numbers of its own, so restoring before Directory.Packages.props
#    is present fails with NU1008 - a failure that never reproduces locally,
#    where the file is always there.
# ---------------------------------------------------------------------------
COPY global.json Directory.Build.props Directory.Build.targets Directory.Packages.props ./

# ---------------------------------------------------------------------------
# 2. Project files only, so `dotnet restore` is cached until a dependency
#    actually changes.
# ---------------------------------------------------------------------------
COPY src/Shared.Kernel/StockPortfolio.Shared.Kernel.csproj                 src/Shared.Kernel/
COPY src/Shared.Api/StockPortfolio.Shared.Api.csproj     src/Shared.Api/
COPY src/Api/StockPortfolio.Api.csproj                                     src/Api/

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

RUN dotnet restore src/Api/StockPortfolio.Api.csproj

# ---------------------------------------------------------------------------
# 3. Sources, then publish. `-o /app` is explicit because Directory.Build.props
#    sets <ArtifactsPath>, so output does NOT land in bin/Release/net10.0/publish.
#    src/Web is deliberately not copied - a frontend edit must not invalidate
#    this build.
# ---------------------------------------------------------------------------
COPY src/Shared.Kernel/       src/Shared.Kernel/
COPY src/Shared.Api/ src/Shared.Api/
COPY src/Modules/            src/Modules/
COPY src/Api/                src/Api/

RUN dotnet publish src/Api/StockPortfolio.Api.csproj \
        --configuration Release \
        --no-restore \
        --output /app


FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .

# The base image CREATES the `app` user but only the -chiseled variants SET it.
# Without this line the container runs as root.
USER app

# ASP.NET Core listens on 8080, not 80 (and targetPort: 8080 in Bicep).
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "StockPortfolio.Api.dll"]
