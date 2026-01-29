# Hosted Agent Dockerfile
# Azure AI Foundry で実行されるコンテナ化エージェント
# マルチステージビルドで最適化

# ===== Build Stage =====
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Copy project file and restore
COPY HostedAgent.csproj .
RUN dotnet restore

# Copy source and build
COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

# ===== Runtime Stage =====
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS runtime
WORKDIR /app

# Copy published app
COPY --from=build /app/publish .

# Expose port (Hosting Adapter default)
EXPOSE 8088

# Environment variables (overridden at deployment)
ENV ASPNETCORE_URLS=http://+:8088
ENV DOTNET_RUNNING_IN_CONTAINER=true

# Entry point
ENTRYPOINT ["dotnet", "HostedAgent.dll"]
