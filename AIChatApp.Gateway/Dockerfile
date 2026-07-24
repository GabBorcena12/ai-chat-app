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

# --- Usage ---
# Build:
# docker build -t aichatapp-gateway -f AIChatApp.Gateway/Dockerfile .
#
# Run:
#docker run -d --name aichatapp-web --network inventory-network -p 4000:8080 `
#  -e Frontend__GatewayBaseUrl=http://aichatapp-gateway:8080/ `
#  -e Frontend__ApiClientName=GajiTechClient `
#  -e Frontend__ApiKey=gaji-tech-secret-key-endpoint-0001 `
#  aichatapp-web
