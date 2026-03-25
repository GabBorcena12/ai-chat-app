# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:9.0-nanoserver-1809 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:9.0-nanoserver-1809 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["AIChatApp.Gateway/AIChatApp.Gateway.csproj", "AIChatApp.Gateway/"]
RUN dotnet restore "./AIChatApp.Gateway/AIChatApp.Gateway.csproj"
COPY . .
WORKDIR "/src/AIChatApp.Gateway"
RUN dotnet build "./AIChatApp.Gateway.csproj" -c %BUILD_CONFIGURATION% -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./AIChatApp.Gateway.csproj" -c %BUILD_CONFIGURATION% -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "AIChatApp.Gateway.dll"]

# --- Usage ---
#docker build -t aichatapp-gateway -f AIChatApp.Gateway/Dockerfile .
#docker run -d --name aichatapp-gateway --network inventory-network -p 5001:8080 aichatapp-gateway