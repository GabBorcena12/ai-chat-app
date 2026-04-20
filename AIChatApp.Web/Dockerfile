# --- Build stage ---
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy Web project file
COPY ["AIChatApp.Web/AIChatApp.Web.csproj", "AIChatApp.Web/"]

# Restore dependencies
RUN dotnet restore "AIChatApp.Web/AIChatApp.Web.csproj"

# Copy full source
COPY . .

# Build and publish Web app
WORKDIR "/src/AIChatApp.Web"
RUN dotnet publish -c Release -o /app/publish

# --- Runtime stage ---
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app

# Copy published output
COPY --from=build /app/publish .

# Expose container ports
EXPOSE 8080
EXPOSE 8081

# Run Web app
ENTRYPOINT ["dotnet", "AIChatApp.Web.dll"]

# --- Usage ---
# Build:
# docker build -t aichatapp-web -f AIChatApp.Web/Dockerfile .
#
# Run:
# docker run -d --name aichatapp-web --network inventory-network -p 4001:8080 ^
#   -e ASPNETCORE_ENVIRONMENT=Development ^
#   aichatapp-web
