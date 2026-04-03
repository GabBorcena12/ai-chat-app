# --- Build stage ---
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj files
COPY ["AIChatApp.API/AIChatApp.API.csproj", "AIChatApp.API/"]
COPY ["AIChatApp.Core/AIChatApp.Core.csproj", "AIChatApp.Core/"]

# Restore dependencies
RUN dotnet restore "AIChatApp.API/AIChatApp.API.csproj"

# Copy all source code
COPY . .

# Build and publish the API
WORKDIR "/src/AIChatApp.API"
RUN dotnet publish -c Release -o /app/publish

# --- Runtime stage ---
FROM mcr.microsoft.com/dotnet/aspnet:9.0
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
ENTRYPOINT ["dotnet", "AIChatApp.API.dll"]

# --- Usage ---
# Build:
# docker build -t aichatapp-api -f AIChatApp.API/Dockerfile .
#
# Run:
# docker run -d --name aichatapp-api --network inventory-network -p 7001:8080 `
#   -e JwtSettings__SecretKey="replace-with-a-long-random-secret-key" `
#   -e ApiKey.Settings__Keys__0="replace-with-your-api-key" `
#   -e ConnectionStrings__DefaultConnection="Server=dummy_server;Database=AIChatAppDb;User Id=sa;Password=Dummy_Passw0rd;TrustServerCertificate=True;" `
#   -e ConnectionStrings__InventoryAppDb="Server=dummy_server;Database=InventoryDb;User Id=sa;Password=Dummy_Passw0rd;TrustServerCertificate=True;" `
#   -e EmailSettings__From="your-email@example.com" `
#   -e EmailSettings__SmtpServer="smtp.gmail.com" `
#   -e EmailSettings__Username="your-email@example.com" `
#   -e EmailSettings__AppPassword="replace-with-your-smtp-password" `
#   -v "C:/Users/gabri/source/repos/AIChatApp/AIChatApp.Core/Models:/app/Models"`
#   aichatapp-api
