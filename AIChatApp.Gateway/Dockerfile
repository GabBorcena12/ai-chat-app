# --- Build stage ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files
COPY ["AIChatApp.Gateway/AIChatApp.Gateway.csproj", "AIChatApp.Gateway/"]
COPY ["AIChatApp.Core/AIChatApp.Core.csproj", "AIChatApp.Core/"]

# Restore dependencies
RUN dotnet restore "AIChatApp.Gateway/AIChatApp.Gateway.csproj"

# Copy full source
COPY . .

# Build & publish Gateway
WORKDIR "/src/AIChatApp.Gateway"
RUN dotnet publish -c Release -o /app/publish

# --- Runtime stage ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Copy published output
COPY --from=build /app/publish .

# Expose ports (YARP / Gateway)
EXPOSE 8080

# Run Gateway
ENTRYPOINT ["dotnet", "AIChatApp.Gateway.dll"]

# Runtime configuration is supplied by docker-compose.yml.
# From the repository root, configure .env and run: docker compose up --build
# Never bake credentials into this image or pass them as Docker build arguments.
