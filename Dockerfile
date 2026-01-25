# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files
COPY Bdaya.Abp.BackgroundJobs.PubSub.csproj .
COPY Directory.Build.props .
COPY Directory.Packages.props .
COPY nuget.config .
COPY global.json .

# Restore dependencies
RUN dotnet restore Bdaya.Abp.BackgroundJobs.PubSub.csproj

# Copy source code
COPY *.cs .
COPY README.md .

# Build and pack
ARG VERSION=1.0.0
RUN dotnet build Bdaya.Abp.BackgroundJobs.PubSub.csproj -c Release --no-restore /p:Version=${VERSION}
RUN dotnet pack Bdaya.Abp.BackgroundJobs.PubSub.csproj -c Release --no-build -o /packages /p:Version=${VERSION}

# Final stage - minimal image with just the package
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS publish
WORKDIR /packages
COPY --from=build /packages/*.nupkg .

# Entry point for publishing
# Usage: docker run -e NUGET_API_KEY=xxx -e NUGET_SOURCE=https://api.nuget.org/v3/index.json <image>
ENTRYPOINT ["sh", "-c", "dotnet nuget push *.nupkg --api-key $NUGET_API_KEY --source ${NUGET_SOURCE:-https://api.nuget.org/v3/index.json} --skip-duplicate"]
