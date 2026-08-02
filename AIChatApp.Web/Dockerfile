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

# Runtime configuration is supplied by docker-compose.yml.
# From the repository root, configure .env and run: docker compose up --build
# Never bake credentials into this image or pass them as Docker build arguments.
