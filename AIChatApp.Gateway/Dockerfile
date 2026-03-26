# --- Build stage ---
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy Gateway project file
COPY ["AIChatApp.Gateway/AIChatApp.Gateway.csproj", "AIChatApp.Gateway/"]

# Restore dependencies
RUN dotnet restore "AIChatApp.Gateway/AIChatApp.Gateway.csproj"

# Copy full source
COPY . .

# Build & publish Gateway
WORKDIR "/src/AIChatApp.Gateway"
RUN dotnet publish -c Release -o /app/publish

# --- Runtime stage ---
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app

# Copy published output
COPY --from=build /app/publish .

# Expose ports (YARP / Gateway)
EXPOSE 8080
EXPOSE 8081

# Run Gateway
ENTRYPOINT ["dotnet", "AIChatApp.Gateway.dll"]

# --- Usage ---
# docker build -t aichatapp-gateway -f AIChatApp.Gateway/Dockerfile .
# docker run -d --name aichatapp-gateway --network inventory-network -p 5001:8080 aichatapp-gateway