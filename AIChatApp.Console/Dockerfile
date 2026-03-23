# --- Build stage ---
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj files
COPY ["AIChatApp.Console/AIChatApp.Console.csproj", "AIChatApp.Console/"]
COPY ["AIChatApp.Core/AIChatApp.Core.csproj", "AIChatApp.Core/"]

# Restore dependencies
RUN dotnet restore "AIChatApp.Console/AIChatApp.Console.csproj"

# Copy all source code
COPY . .

# Build and publish the console app
WORKDIR "/src/AIChatApp.Console"
RUN dotnet publish -c Release -o /app/publish

# --- Runtime stage ---
FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /app

# Install native dependencies for LLamaSharp CPU backend
RUN apt-get update && \
    apt-get install -y libgomp1 libomp-dev && \
    rm -rf /var/lib/apt/lists/*

# Copy published app from build stage
COPY --from=build /app/publish .

# Copy Core files (Models and Data) into container
COPY AIChatApp.Core/Models /app/AIChatApp.Core/Models
COPY AIChatApp.Core/Data /app/AIChatApp.Core/Data

# Ensure folders exist (optional, safe)
RUN mkdir -p /app/AIChatApp.Core/Models
RUN mkdir -p /app/AIChatApp.Core/Data

# Set entrypoint
ENTRYPOINT ["dotnet", "AIChatApp.Console.dll"]

# --- Usage ---
#docker build -t aichatapp-console -f AIChatApp.Console/Dockerfile .
#docker run -it --name aichatapp-console --network inventory-network -p 6001:8080 -v "C:/Users/gabri/source/repos/AIChatApp/AIChatApp.Core/Models:/app/Models" aichatapp-console
#docker attach aichatapp-console