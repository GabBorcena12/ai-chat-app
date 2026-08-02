# --- Build stage ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files
COPY ["AIChatApp.API/AIChatApp.API.csproj", "AIChatApp.API/"]
COPY ["AIChatApp.Core/AIChatApp.Core.csproj", "AIChatApp.Core/"]
COPY ["AIChatApp.MLTraining/AIChatApp.MLTraining.csproj", "AIChatApp.MLTraining/"]

# Restore dependencies
RUN dotnet restore "AIChatApp.API/AIChatApp.API.csproj"

# Copy all source code
COPY . .

# Build and publish the API
WORKDIR "/src/AIChatApp.API"
RUN dotnet publish -c Release -o /app/publish

# --- Runtime stage ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Install native dependencies for LLamaSharp CPU backend
RUN apt-get update && \
    apt-get install -y libgomp1 libomp-dev && \
    rm -rf /var/lib/apt/lists/*

# Copy published app from build stage
COPY --from=build /app/publish .

# Copy Core data and reviewer files into container.
# GGUF model files are mounted by docker-compose to keep the image small.
COPY AIChatApp.Core/Data /app/AIChatApp.Core/Data
COPY AIChatApp.MLTraining/ReviewerModels /app/AIChatApp.MLTraining/ReviewerModels
COPY README.md /app/README.md

# Ensure folders exist (optional, safe)
RUN mkdir -p /app/AIChatApp.Core/Models
RUN mkdir -p /app/AIChatApp.Core/Data
RUN mkdir -p /app/AIChatApp.MLTraining/ReviewerModels/Candidates

EXPOSE 8080

# Set entrypoint
ENTRYPOINT ["dotnet", "AIChatApp.API.dll"]

# Runtime configuration is supplied by docker-compose.yml.
# From the repository root, configure .env and run: docker compose up --build
# Never bake credentials into this image or pass them as Docker build arguments.
