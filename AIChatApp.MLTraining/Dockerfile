# --- Build stage ---
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["AIChatApp.MLTraining/AIChatApp.MLTraining.csproj", "AIChatApp.MLTraining/"]
COPY ["AIChatApp.MLTraining.Core/AIChatApp.MLTraining.Core.csproj", "AIChatApp.MLTraining.Core/"]

RUN dotnet restore "AIChatApp.MLTraining/AIChatApp.MLTraining.csproj"

COPY . .

WORKDIR "/src/AIChatApp.MLTraining"
RUN dotnet publish "AIChatApp.MLTraining.csproj" -c Release -o /app/publish --no-restore

# --- Runtime stage ---
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# Keep reviewer models outside the image so trained/published models survive container rebuilds.
RUN mkdir -p /app/AIChatApp.MLTraining.Core/ReviewerModels/Candidates

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "AIChatApp.MLTraining.dll"]

# Build:
# docker build -t aichatapp-mltraining -f AIChatApp.MLTraining/Dockerfile .
#
# Run example:
# docker run --rm --name aichatapp-mltraining --network inventory-network -p 55192:8080 `
#   -e TrainingFrontend__GatewayBaseUrl="http://aichatapp-gateway:8080/" `
#   -e TrainingFrontend__ChatAppUrl="http://localhost:4000/chat" `
#   -e TrainingFrontend__BackofficeUrl="http://localhost:4000/backoffice" `
#   -e TrainingFrontend__FaqsUrl="http://localhost:4000/faqs" `
#   aichatapp-mltraining
