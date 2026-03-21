# ---------- Base stage ----------
FROM mcr.microsoft.com/dotnet/runtime:9.0-nanoserver-1809 AS base
WORKDIR /app

# ---------- Build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:9.0-nanoserver-1809 AS build
ARG BUILD_CONFIGURATION=Release
ARG PROJECT_TO_BUILD=AIChatApp.Core  # default project

WORKDIR /src

# Copy all csproj files first for caching
COPY ["AIChatApp.Core/AIChatApp.Core.csproj", "AIChatApp.Core/"]
COPY ["AIChatApp.API/AIChatApp.API.csproj", "AIChatApp.API/"]
COPY ["AIChatApp.Console/AIChatApp.Console.csproj", "AIChatApp.Console/"]

# Restore dependencies for the selected project
RUN dotnet restore "${PROJECT_TO_BUILD}/${PROJECT_TO_BUILD}.csproj"

# Copy all source files
COPY . .

# Build the selected project
RUN dotnet build "${PROJECT_TO_BUILD}/${PROJECT_TO_BUILD}.csproj" -c %BUILD_CONFIGURATION% -o /app/build

# ---------- Publish stage ----------
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
ARG PROJECT_TO_BUILD=AIChatApp.Core

RUN dotnet publish "${PROJECT_TO_BUILD}/${PROJECT_TO_BUILD}.csproj" -c %BUILD_CONFIGURATION% -o /app/publish /p:UseAppHost=false

# ---------- Final stage ----------
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Run the selected project
ARG PROJECT_TO_BUILD=AIChatApp.Core
ENTRYPOINT ["dotnet", "AIChatApp.Core.dll"]