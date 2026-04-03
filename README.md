# ChatApp-Meta-Llama

A local AI chat application built on .NET 9, LLamaSharp, SQL Server, JWT authentication, and Google Authenticator-style TOTP 2FA.

## Overview

This solution contains four main projects:

- `AIChatApp.API`: backend for authentication, chat orchestration, chat history, and LLM access
- `AIChatApp.Gateway`: reverse proxy entry point with API key validation and rate limiting
- `AIChatApp.Core`: shared config, middleware, data access, and model path helpers
- `AIChatApp.Console`: local console chat client for direct model testing

## Requirements

- .NET 9 SDK and runtime
- SQL Server, either local or containerized
- A Meta Llama 3.1 GGUF model file
- A client such as Postman, Bruno, or a frontend app for calling the API

## Setup

### 1. Download the model

Download the GGUF model from Hugging Face:

[Meta-Llama-3.1-8B-Instruct-Q4_K_M-GGUF](https://huggingface.co/joshnader/Meta-Llama-3.1-8B-Instruct-Q4_K_M-GGUF/blob/main/meta-llama-3.1-8b-instruct-q4_k_m.gguf)

### 2. Place the model file

Copy the `.gguf` file into:

```text
AIChatApp/AIChatApp.Core/Models
```

### 3. Configure the application

Review the API and gateway configuration files:

```text
AIChatApp.API/appsettings.json
AIChatApp.API/appsettings.Development.json
AIChatApp.Gateway/appsettings.json
```

Committed `appsettings` files keep non-sensitive defaults in source control and use dummy placeholders for sensitive values. Load real secrets through ASP.NET Core user secrets for local development and environment variables for Docker or production.

Important secret settings include:

- `JwtSettings:SecretKey`
- `ApiKey.Settings:Keys`
- `ConnectionStrings:DefaultConnection` when it includes SQL credentials
- `ConnectionStrings:InventoryAppDb` when it includes SQL credentials
- `EmailSettings:AppPassword`

These values are usually safe to keep in committed config:

- `AppSettings:BaseUrl`
- `JwtSettings:Issuer`
- `JwtSettings:Audience`
- `ApiKey.Settings:ClientName`
- `EmailSettings:SmtpServer`
- `EmailSettings:Port`
- local development connection strings that do not contain passwords

Example local development setup:

```powershell
dotnet user-secrets init --project AIChatApp.API
dotnet user-secrets set "JwtSettings:SecretKey" "replace-with-a-long-random-secret-key" --project AIChatApp.API
dotnet user-secrets set "ApiKey.Settings:Keys:0" "replace-with-your-api-key" --project AIChatApp.API
dotnet user-secrets set "EmailSettings:AppPassword" "replace-with-your-smtp-password" --project AIChatApp.API

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

Request body:

```json
{
  "chatId": "000001",
  "user": "John Doe",
  "prompt": "Hello AI"
}
```

This endpoint requires:

- `Authorization: Bearer <jwt>`
- gateway headers if called through the gateway

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
  "prompt": "Hello AI!"
}
```

## Notes

- `ask-stream` currently returns a normal JSON response body, even though its route name suggests streaming.
- The gateway maps `/chat/*`, `/auth/*`, and `/chathistory/*` to the API service.
- Google Authenticator support is based on TOTP with 6-digit codes and 30-second time windows.
- Rotate any secrets that were previously committed to the repository before using this project in a shared environment.
