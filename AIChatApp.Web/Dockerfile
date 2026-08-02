# --- Build stage ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files
COPY ["AIChatApp.Web/AIChatApp.Web.csproj", "AIChatApp.Web/"]
COPY ["AIChatApp.Core/AIChatApp.Core.csproj", "AIChatApp.Core/"]
COPY ["AIChatApp.MLTraining/AIChatApp.MLTraining.csproj", "AIChatApp.MLTraining/"]

# Restore dependencies
RUN dotnet restore "AIChatApp.Web/AIChatApp.Web.csproj"

# Copy full source
COPY . .

# Build and publish Web app
WORKDIR "/src/AIChatApp.Web"
RUN dotnet publish -c Release -o /app/publish

# --- Runtime stage ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Copy published output
COPY --from=build /app/publish .
COPY AIChatApp.Core/Data /app/AIChatApp.Core/Data
COPY AIChatApp.MLTraining/ReviewerModels /app/AIChatApp.MLTraining/ReviewerModels

# Expose container ports
EXPOSE 8080

# Run Web app
ENTRYPOINT ["dotnet", "AIChatApp.Web.dll"]

# --- Usage ---
# Build:
# docker build -t aichatapp-web -f AIChatApp.Web/Dockerfile .
#
# Run:
# docker run -d --name aichatapp-web --network aichatapp-network -p 4001:8080 ^
#   -e ASPNETCORE_ENVIRONMENT=Development ^
#   aichatapp-web
