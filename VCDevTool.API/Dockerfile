# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files
COPY ["VCDevTool.API/VCDevTool.API.csproj", "VCDevTool.API/"]
COPY ["VCDevTool.Shared/VCDevTool.Shared.csproj", "VCDevTool.Shared/"]

# Restore dependencies
RUN dotnet restore "VCDevTool.API/VCDevTool.API.csproj"

# Copy source code
COPY . .

# Build application
WORKDIR "/src/VCDevTool.API"
RUN dotnet build "VCDevTool.API.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "VCDevTool.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Create non-root user for security
RUN groupadd -r vcdevtool && useradd -r -g vcdevtool vcdevtool

# Create directories and set permissions
RUN mkdir -p /app/logs && \
    mkdir -p /var/log/vcdevtool && \
    chown -R vcdevtool:vcdevtool /app && \
    chown -R vcdevtool:vcdevtool /var/log/vcdevtool

# Copy published application
COPY --from=publish /app/publish .

# Switch to non-root user
USER vcdevtool

# Expose port
EXPOSE 5289

# Set environment
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:5289

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:5289/health || exit 1

# Start application
ENTRYPOINT ["dotnet", "VCDevTool.API.dll"] 