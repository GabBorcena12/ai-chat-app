# AIChatApp

A local AI chat application built on .NET 9, LLamaSharp, SQL Server, JWT authentication, Google Authenticator-style TOTP 2FA, and a Blazor frontend.

AIChatApp is a full-stack AI chat platform built with Blazor, ASP.NET Core, YARP, SQL Server, JWT authentication, Google Authenticator 2FA, and local GGUF model inference through LLamaSharp. The solution supports streaming chat over Server-Sent Events, continuation handling for cut-off responses, JSON-based prompt and knowledge configuration, assistant-profile-driven behavior, browser-persisted conversations, response reporting, and Docker-based local deployment.

## Overview

This solution contains seven main projects:

- `AIChatApp.API`: backend for authentication, chat orchestration, chat history, and LLM access
- `AIChatApp.Gateway`: reverse proxy entry point with API key validation and rate limiting
- `AIChatApp.Core`: shared config, middleware, data access, and model path helpers
- `AIChatApp.Console`: local console chat client for direct model testing
- `AIChatApp.Web`: Blazor frontend for login, 2FA management, conversation UI, and streaming chat
- `AIChatApp.MLTraining`: optional standalone Blazor UI for reviewer training experiments
- `AIChatApp.MLTraining.Core`: shared ML.NET reviewer models, trainer, runtime reviewer service, and training workflow services

## Key Features

- Local LLM inference using GGUF models configured through `LocalModel:FileName` and `LocalModel:ContextSize`
- Streaming chat with `ask-stream` over Server-Sent Events
- Non-streaming JSON chat with `ask-ai`
- Continuation flow with `ask-continue` for cut-off responses
- JWT-authenticated API access with Google Authenticator TOTP 2FA
- Gateway-side API key checks, route forwarding, and rate limiting with YARP
- Assistant-profile-based prompt and knowledge loading
- JSON-based prompt templates, quick answers, topic knowledge, and console/shared context files
- Retrieval-style documentation context selection from focused knowledge sources
- Browser-persisted conversation workspace with rename, delete, timestamps, copy, continue, and report actions
- Response reporting pipeline for saving bad or suspicious AI answers for later investigation
- Admin backoffice for reviewing reported answers, validating fixes, managing prompt templates, and maintaining assistant knowledge in SQL
- ML.NET reviewer workflow for training a response-quality classifier from approved backoffice reports
- Runtime response-quality review before answers are returned, with rule fallback when no ML.NET model is published
- Docker-ready local deployment for API, gateway, and web application workflows

## Resume Summary

Built a full-stack AI chat platform in .NET with a Blazor frontend, ASP.NET Core API, YARP-based gateway, SQL Server persistence, JWT authentication, Google Authenticator 2FA, and local LLM inference using GGUF models through LLamaSharp. Designed a profile-driven assistant architecture with JSON-based prompt and knowledge configuration, retrieval-style documentation context loading, streaming chat via Server-Sent Events, continuation handling for truncated responses, browser-persisted conversation state, response reporting, and Docker-based local deployment support.

## Backoffice Guide

The backoffice is the admin workflow for improving assistant quality without editing code files directly.

### What It Is For

- reviewing reported bad answers from real user chats
- validating corrected question and response pairs
- promoting validated fixes into reusable assistant knowledge
- editing published prompt templates that shape assistant behavior
- managing published knowledge entries such as quick answers, topics, and references
- managing users and assigning backoffice roles such as `Admin`, `DataValidator`, and `AppUser`
- building, training, and publishing the ML.NET reviewer from approved report examples

### Main Workflow

1. A user reports a bad or suspicious response from the chat UI.
2. The backend saves the report with the original prompt, assistant response, and metadata.
3. An admin opens `/backoffice` and reviews the report in `Reported Responses`.
4. The admin updates the validated question and validated response, then chooses a review status and category.
5. If the fix should improve future answers, the admin checks `Promote to knowledge`.
6. If the new knowledge should become live right away, the admin also checks `Publish immediately`.
7. Approved reports with a category become training candidates.
8. In `Workflow`, the admin clicks `Build Dataset`.
9. The admin clicks `Train Model` to train the ML.NET reviewer classifier.
10. The admin reviews the accuracy/F1 result.
11. The admin clicks `Publish Reviewer`.
12. The API loads the published reviewer and classifies generated answers before they are returned.

### ML.NET Reviewer Workflow

The ML.NET reviewer does not generate chat answers. Qwen/Llama still generates responses. The reviewer checks the generated answer and predicts whether it looks `Good`, `Incomplete`, `Repetitive`, `PromptLeak`, `TooLong`, or `Incorrect`.

High-level flow:

1. User reports a bad response.
2. Admin validates the response in Backoffice.
3. Approved reports become training candidates.
4. Admin builds a dataset from approved examples.
5. Admin trains the ML.NET reviewer.
6. Admin checks the accuracy/F1 result.
7. Admin publishes the reviewer model.
8. API uses the published reviewer before returning future LLM answers.

Reviewer behavior:

- if a published ML.NET model exists, the API uses it for response-quality classification
- if no model is published yet, the API still uses rule-based fallback checks
- risky documentation answers can be retried or cleaned before the user sees them
- the reviewer is a quality gate, not a response generator

### Backoffice Sections

- `Workflow`
  - shows the full improvement loop from reported answer to published reviewer
  - displays counts for pending reports, approved fixes, training candidates, and live knowledge
  - provides `Build Dataset`, `Train Model`, and `Publish Reviewer` controls
  - lists training candidates created from approved reviewed reports
- `Reported Responses`
  - list view of reported answers
  - filter by status such as needs action, reviewed, approved, or rejected
  - open a review popup to validate and optionally promote corrections
- `Prompt Templates`
  - database-backed assistant behavior rules such as system context, answer style, retry template, and continuation template
- `Knowledge Entries`
  - database-backed quick answers, topics, and reference content used by the assistant
- `Profile Management`
  - update the chat display name and avatar label
- `User Management`
  - create users from the backoffice
  - assign `Admin`, `DataValidator`, and `AppUser` roles
  - enable or disable users and manage confirmation state
  - assign `Admin`, `DataValidator`, and `AppUser` roles
  - enable or disable users and manage confirmation state

### Roles

- `Admin`
  - full backoffice access including prompt templates, knowledge, reports, and user management
- `DataValidator`
  - intended for review and validation workflows in the backoffice
- `AppUser`
  - default application user role for normal chat usage

### Default Admin Seeding

On a fresh project startup, the API can seed a default admin account and required roles automatically.

Relevant API config keys:

```json
"Backoffice": {
  "AdminUsernames": ["gabrielborcena12"],
  "SeedDefaultAdmin": true,
  "DefaultAdminUsername": "admin",
  "DefaultAdminEmail": "admin@localhost",
  "DefaultAdminPassword": "Admin123!"
}
```

Change the default admin password before using the project outside local development.

### Promote Vs Publish

- `Promote to knowledge`
  - creates a reusable knowledge entry from a reviewed report
- `Publish immediately`
  - makes that new knowledge entry active right away
- `Published`
  - means a prompt template or knowledge entry is live and available to the assistant

## Requirements

- .NET 9 SDK and runtime
- SQL Server, either local or containerized
- A compatible GGUF model file for local inference
- A client such as Postman, Bruno, or the included Blazor frontend for calling the API

## Setup

### 1. Download a GGUF model

Download the GGUF model used by this project from Hugging Face:

- [Qwen2.5-3B-Instruct-GGUF](https://huggingface.co/Qwen/Qwen2.5-3B-Instruct-GGUF)

Use this file name in the project:

- `qwen2.5-3b-instruct-q4_k_m.gguf`

### 2. Place the model file

Copy the `.gguf` file into:

```text
AIChatApp/AIChatApp.Core/Models
```

### 3. Configure the application

Review the API, gateway, and web configuration files:

```text
AIChatApp.API/appsettings.json
AIChatApp.API/appsettings.Development.json
AIChatApp.Gateway/appsettings.json
AIChatApp.Gateway/appsettings.Development.json
AIChatApp.Web/appsettings.json
AIChatApp.Web/appsettings.Development.json
```

Committed `appsettings` files keep non-sensitive defaults in source control and use dummy placeholders for sensitive values. Load real secrets through ASP.NET Core user secrets for local development and environment variables for Docker or production.

Recommended split:

- keep non-sensitive structure and defaults in `appsettings.json`
- keep developer-local overrides in `appsettings.Development.json`
- keep real secrets out of source control

Important secret settings include:

- `JwtSettings:SecretKey`
- `ApiKey.Settings:Keys:0`
- `ConnectionStrings:DefaultConnection` when it includes SQL credentials
- `ConnectionStrings:InventoryAppDb` when it includes SQL credentials
- `EmailSettings:AppPassword`
- `Backoffice:DefaultAdminPassword` if you override it with a real value

These values are usually safe to keep in committed config:

- `AppSettings:BaseUrl`
- `JwtSettings:Issuer`
- `JwtSettings:Audience`
- `ApiKey.Settings:ClientName`
- `Backoffice:AdminUsernames`
- `Backoffice:SeedDefaultAdmin`
- `Backoffice:DefaultAdminUsername`
- `Backoffice:DefaultAdminEmail`
- `EmailSettings:SmtpServer`
- `EmailSettings:Port`
- local development connection strings that do not contain passwords

The local model is configured in API config:

- `LocalModel:FileName`
- `LocalModel:ContextSize`

Example:

```json
"LocalModel": {
  "FileName": "qwen2.5-3b-instruct-q4_k_m.gguf",
  "ContextSize": 5000
}
```

This README assumes `qwen2.5-3b-instruct-q4_k_m.gguf` is the default local model. Keep the file under `AIChatApp.Core/Models` and point `LocalModel:FileName` at that exact filename.

The assistant prompt and knowledge system is now stored as JSON under:

```text
AIChatApp.Core/Data/Assistants/<ProfileId>/Prompts
AIChatApp.Core/Data/Assistants/<ProfileId>/Knowledge
```

Examples:

```text
AIChatApp.Core/Data/Assistants/Documentation/Prompts/SystemContext.json
AIChatApp.Core/Data/Assistants/Documentation/Knowledge/QuickAnswers.json
AIChatApp.Core/Data/Assistants/Documentation/Knowledge/Faq.json
```

Console-specific and shared content is also JSON-based:

```text
AIChatApp.Core/Data/Console/system_context.json
AIChatApp.Core/Data/Console/product_knowledge.json
AIChatApp.Core/Data/Console/disallowed_topics.json
AIChatApp.Core/Data/Shared/system_api_context.json
```

Example local development setup:

```powershell
dotnet user-secrets init --project AIChatApp.API
dotnet user-secrets set "JwtSettings:SecretKey" "replace-with-a-long-random-secret-key" --project AIChatApp.API
dotnet user-secrets set "ApiKey.Settings:Keys:0" "replace-with-your-api-key" --project AIChatApp.API
dotnet user-secrets set "EmailSettings:AppPassword" "replace-with-your-smtp-password" --project AIChatApp.API
dotnet user-secrets set "Backoffice:DefaultAdminPassword" "replace-with-a-strong-admin-password" --project AIChatApp.API

dotnet user-secrets init --project AIChatApp.Gateway
dotnet user-secrets set "ApiKey.Settings:Keys:0" "replace-with-your-api-key" --project AIChatApp.Gateway
```

If you want to override the default local connection strings too, you can still set them through user secrets.

Example Docker environment variables for sensitive values only:

```text
ConnectionStrings__DefaultConnection=Server=host.docker.internal,1433;Database=AIChatAppDb;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True;
ConnectionStrings__InventoryAppDb=Server=host.docker.internal,1433;Database=InventoryDb;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True;
JwtSettings__SecretKey=replace-with-a-long-random-secret-key
ApiKey.Settings__Keys__0=replace-with-your-api-key
EmailSettings__AppPassword=replace-with-your-smtp-password
Backoffice__DefaultAdminPassword=replace-with-a-strong-admin-password
```

This repo also includes:

- [.env.example](C:\Users\gabri\source\repos\AIChatApp\.env.example) as a reference for the environment variables the containers expect

Quick Docker setup:

1. Build the API image:

```powershell
docker build -t aichatapp-api -f AIChatApp.API/Dockerfile .
```

2. Build the Gateway image:

```powershell
docker build -t aichatapp-gateway -f AIChatApp.Gateway/Dockerfile .
```

3. Run the API container with the required environment variables.

If SQL Server is running on your Windows host, use `host.docker.internal`:

```powershell
docker run -d --name aichatapp-api -p 7001:8080 `
  -e JwtSettings__SecretKey="replace-with-a-long-random-secret-key" `
  -e ApiKey.Settings__Keys__0="replace-with-your-api-key" `
  -e ConnectionStrings__DefaultConnection="Server=host.docker.internal,1433;Database=AIChatAppDb;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True;" `
  -e ConnectionStrings__InventoryAppDb="Server=host.docker.internal,1433;Database=InventoryDb;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True;" `
  -e EmailSettings__From="your-email@example.com" `
  -e EmailSettings__SmtpServer="smtp.gmail.com" `
  -e EmailSettings__Username="your-email@example.com" `
  -e EmailSettings__AppPassword="replace-with-your-smtp-password" `
  aichatapp-api
```

If SQL Server is running in a Docker container instead, place both containers on the same network and use the SQL container name such as `mssqlserver`:

```powershell
docker network create inventory-network
docker network connect inventory-network mssqlserver

docker run -d --name aichatapp-api --network inventory-network -p 7001:8080 `
  -e JwtSettings__SecretKey="replace-with-a-long-random-secret-key" `
  -e ApiKey.Settings__Keys__0="replace-with-your-api-key" `
  -e ConnectionStrings__DefaultConnection="Server=mssqlserver,1433;Database=AIChatAppDb;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True;" `
  -e ConnectionStrings__InventoryAppDb="Server=mssqlserver,1433;Database=InventoryDb;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True;" `
  -e EmailSettings__From="your-email@example.com" `
  -e EmailSettings__SmtpServer="smtp.gmail.com" `
  -e EmailSettings__Username="your-email@example.com" `
  -e EmailSettings__AppPassword="replace-with-your-smtp-password" `
  aichatapp-api
```

4. Run the Gateway container:

```powershell
docker run -d --name aichatapp-gateway -p 5001:8080 `
  -e ApiKey.Settings__Keys__0="replace-with-your-api-key" `
  aichatapp-gateway
```

If you are also using a shared Docker network for SQL Server, run the gateway on that same network:

```powershell
docker run -d --name aichatapp-gateway --network inventory-network -p 5001:8080 `
  -e ApiKey.Settings__Keys__0="replace-with-your-api-key" `
  aichatapp-gateway
```

5. Open the API at `http://localhost:7001`
6. Open the Gateway at `http://localhost:5001`

If you are running the projects locally with `dotnet run` instead of Docker, the default development ports are:

- API: `https://localhost:7093` and `http://localhost:5157`
- Gateway: `https://localhost:7067` and `http://localhost:5031`
- Web: `https://localhost:7033` and `http://localhost:5143`
- Web routes: `/chat`, `/backoffice`, `/faqs`, and `/ml-training`
- Optional standalone ML Training UI: `http://localhost:55192` in local development. HTTPS `https://localhost:55191` also exists if the dev certificate is trusted.

Important local auth note:

- `AIChatApp.Web/appsettings.Development.json` should point `Frontend:GatewayBaseUrl` to `https://localhost:7067/`.
- `AIChatApp.MLTraining/appsettings.Development.json` should point `TrainingFrontend:GatewayBaseUrl` to `https://localhost:7067/`.
- Avoid using `http://localhost:5031/` for authenticated Web chat calls because Gateway HTTPS redirection can drop the `Authorization: Bearer <token>` header during redirect.
- If chat says the session expired immediately after login, restart API, Gateway, and Web, then sign in again.

PowerShell note: the backtick must be the final character on the line. Do not attach it directly to `8080` or any other value.

### 4. Apply the database

The API currently auto-runs EF Core migrations at startup. You can also apply them manually from the repo root:

```powershell
dotnet ef database update --project AIChatApp.API --startup-project AIChatApp.API
```

### 5. Run the applications

Run the API:

```powershell
dotnet run --project AIChatApp.API
```

Run the gateway:

```powershell
dotnet run --project AIChatApp.Gateway
```

Run the Blazor web app:

```powershell
dotnet run --project AIChatApp.Web
```

Open Chat at `/chat` and ML Training at `/ml-training` on the Web app host.

Optional standalone ML training UI:

```powershell
dotnet run --project AIChatApp.MLTraining
```

Open it locally at `http://localhost:55192`. Use `https://localhost:55191` only after trusting the ASP.NET Core development certificate.

The preferred ML Training UI is now `/ml-training` inside `AIChatApp.Web`, so it uses the same browser session as Chat and Backoffice. The standalone ML Training project remains useful for isolated experiments.

Run the console client:

```powershell
dotnet run --project AIChatApp.Console
```

## Auth And Security

### Gateway headers

Requests passing through the gateway must include:

- `X-Api-Client`
- `X-Api-Key`

Example:

```http
X-Api-Client: GajiTechClient
X-Api-Key: your-api-key
```

### JWT flow

Authentication is handled by `POST /api/auth/login`.

Login request body:

```json
{
  "username": "demo-user",
  "password": "your-password"
}
```

Successful login response:

```json
{
  "token": "your-jwt-token"
}
```

Use the JWT in authenticated API calls:

```http
Authorization: Bearer your-jwt-token
```

When calling through the gateway, authenticated chat endpoints usually require both:

- `Authorization: Bearer <jwt>`
- gateway headers: `X-Api-Client` and `X-Api-Key`

For local Web development, call the HTTPS Gateway URL directly:

```json
{
  "Frontend": {
    "GatewayBaseUrl": "https://localhost:7067/"
  }
}
```

Using the HTTP Gateway URL can cause authenticated chat requests to fail after login if the request is redirected and the bearer token is not forwarded.

### Google Authenticator 2FA flow

The API supports TOTP-based 2FA using Google Authenticator or compatible apps.

Initial setup flow:

1. Log in with username and password to get a JWT.
2. Call `POST /api/auth/2fa/setup` with `Authorization: Bearer <token>`.
3. Read the returned `AuthenticatorUri` or `SharedKey`.
4. Scan the `AuthenticatorUri` as a QR code, or enter the shared key manually, in Google Authenticator.
5. Call `POST /api/auth/2fa/verify` with the current 6-digit code.
6. Future login requests must include `OtpCode`.

Setup response:

```json
{
  "sharedKey": "BASE32SECRET",
  "authenticatorUri": "otpauth://totp/AIChatApp:demo-user?secret=BASE32SECRET&issuer=AIChatApp&digits=6&period=30"
}
```

Verify request:

```json
{
  "code": "123456"
}
```

Login request after 2FA is enabled:

```json
{
  "username": "demo-user",
  "password": "your-password",
  "otpCode": "123456"
}
```

### Change device flow

If a user gets a new phone and needs a new QR code:

1. Sign in from an existing trusted session.
2. Call `POST /api/auth/2fa/setup` again.
3. Render the returned `AuthenticatorUri` as a QR code.
4. Scan it using the new device.
5. Call `POST /api/auth/2fa/verify` using the new device's 6-digit code.

### Disable 2FA

Send the current authenticator code to:

```text
POST /api/auth/2fa/disable
```

Request body:

```json
{
  "code": "123456"
}
```

## Main API Endpoints

### Auth

- `POST /api/auth/register`
- `POST /api/auth/login`
- `POST /api/auth/2fa/setup`
- `POST /api/auth/2fa/verify`
- `POST /api/auth/2fa/disable`
- `POST /api/auth/reset-password`
- `POST /api/auth/test-email-service`

### Chat

- `POST /api/chat/ask-stream`
- `POST /api/chat/ask-ai`
- `POST /api/chat/ask-continue`

`ask-stream` request body:

```json
{
  "chatId": "000001",
  "user": "John Doe",
  "prompt": "Hello AI",
  "contextMode": "documentation"
}
```

`ask-stream` requires:

- `Authorization: Bearer <jwt>`
- gateway headers if called through the gateway

`ask-stream` returns Server-Sent Events with token chunks plus a final `complete` event.

`ask-ai` returns a JSON payload:

```json
{
  "prompt": "Hello AI",
  "response": "Hello! How can I help?"
}
```

`ask-continue` is used to finish a response that was cut off. Request body:

```json
{
  "chatId": "000001",
  "user": "John Doe",
  "originalPrompt": "What is this project used for?",
  "partialResponse": "AIChatApp is a local .NET chat application...",
  "contextMode": "documentation"
}
```

### Chat history

- `GET /api/chathistory/conversations/{chatId}`

This endpoint requires:

- `Authorization: Bearer <jwt>`
- gateway headers if called through the gateway

## Example Requests

### Register

```http
POST /api/auth/register
Content-Type: application/json
```

```json
{
  "username": "demo-user",
  "password": "StrongPassword123!",
  "email": "demo@example.com"
}
```

### Login

```http
POST /api/auth/login
Content-Type: application/json
```

```json
{
  "username": "demo-user",
  "password": "StrongPassword123!"
}
```

### Setup Google Authenticator

```http
POST /api/auth/2fa/setup
Authorization: Bearer your-jwt-token
```

### Verify Google Authenticator

```http
POST /api/auth/2fa/verify
Authorization: Bearer your-jwt-token
Content-Type: application/json
```

```json
{
  "code": "123456"
}
```

### Ask The Chat API Through The Gateway

```http
POST /chat/ask-stream
X-Api-Client: GajiTechClient
X-Api-Key: your-api-key
Authorization: Bearer your-jwt-token
Content-Type: application/json
```

```json
{
  "chatId": "000001",
  "user": "John Doe",
  "prompt": "Hello AI!",
  "contextMode": "documentation"
}
```

### Ask The JSON Chat Endpoint Through The Gateway

```http
POST /chat/ask-ai
X-Api-Client: GajiTechClient
X-Api-Key: your-api-key
Authorization: Bearer your-jwt-token
Content-Type: application/json
```

```json
{
  "chatId": "000001",
  "user": "John Doe",
  "prompt": "What does AIChatApp.API do?",
  "contextMode": "documentation"
}
```

### Continue A Cut Response Through The Gateway

```http
POST /chat/ask-continue
X-Api-Client: GajiTechClient
X-Api-Key: your-api-key
Authorization: Bearer your-jwt-token
Content-Type: application/json
```

```json
{
  "chatId": "000001",
  "user": "John Doe",
  "originalPrompt": "What is this project used for?",
  "partialResponse": "AIChatApp is a local .NET chat application...",
  "contextMode": "documentation"
}
```

## Notes

- `ask-stream` is an SSE endpoint, not a normal JSON endpoint.
- `ask-ai` is the non-streaming JSON endpoint.
- `ask-continue` is the dedicated continuation endpoint for finishing a cut-off answer.
- Prompt and knowledge files are JSON-based under `AIChatApp.Core/Data/...`.
- The documentation assistant uses profile-specific prompt templates, quick answers, topic summaries, and focused knowledge references.
- The Web app is currently pinned to the documentation assistant experience.
- The chat UI supports browser-persisted conversations, inline rename/delete, timestamps, copy, continue, report, completion notifications, and auto-follow scrolling.
- The gateway maps `/chat/*`, `/auth/*`, and `/chathistory/*` to the API service.
- In local development, the Web app should call the HTTPS Gateway URL directly to preserve `Authorization` headers on authenticated chat requests.
- The preferred ML Training UI is hosted by `AIChatApp.Web` at `/ml-training`.
- `AIChatApp.MLTraining` can still run as a standalone experiment UI, but normal navigation should use the Web-hosted `/ml-training` route.
- Google Authenticator support is based on TOTP with 6-digit codes and 30-second time windows.
- The Blazor frontend uses the gateway as its API entry point in development.
- Rotate any secrets that were previously committed to the repository before using this project in a shared environment.
