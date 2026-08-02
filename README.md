# AIChatApp

Last updated: July 24, 2026

AIChatApp is a local .NET 10 documentation chat platform. It combines a Blazor Web UI, ASP.NET Core API, YARP Gateway, SQL Server persistence, JWT authentication, Google Authenticator-compatible 2FA, local GGUF model inference through LLamaSharp, Backoffice knowledge management, and an ML.NET response-quality reviewer workflow.

The app is designed for a project knowledge base: users ask documentation questions in Chat, admins validate reported answers in Backoffice, and approved knowledge can be reused by the assistant without editing code files.

## Projects

- `AIChatApp.Web`
  - Blazor frontend for Chat, Backoffice, FAQs, login, 2FA setup, profile/account modals, and responsive workspace navigation.
  - Routes include `/chat`, `/backoffice`, and `/faqs`.
  - Backoffice includes Validation, Machine Learning, and Administration workflows.
- `AIChatApp.API`
  - ASP.NET Core backend for authentication, chat orchestration, chat history, response reporting, backoffice APIs, prompt/knowledge storage, and local LLM access.
  - Runs EF Core migrations at startup.
- `AIChatApp.Gateway`
  - YARP reverse proxy for API access.
  - Applies API key checks, rate limiting, and route forwarding.
- `AIChatApp.Core`
  - Shared configuration, entities, DbContext, middleware, content path helpers, and assistant JSON models.
- `AIChatApp.MLTraining`
  - ML.NET response reviewer trainer, runtime reviewer service, model options, and Backoffice training workflow services.
- `AIChatApp.Console`
  - Local console client for direct model testing.
- `AIChatApp.Tests`
  - Unit tests, including response reviewer quality tests.

## Current Features

- Local GGUF chat model inference with LLamaSharp.
- Streaming chat through Server-Sent Events.
- Non-streaming JSON chat endpoint.
- Continuation endpoint for cut-off answers.
- JWT login and Google Authenticator-compatible TOTP 2FA.
- Gateway API key validation and rate limiting.
- Browser-persisted conversations with rename, delete, timestamps, copy, read-aloud, report, and continue actions.
- Dark graphite and royal-blue responsive UI theme.
- Responsive Chat and Backoffice side navigation with collapsible groups.
- Backoffice modals for reported responses, prompt templates, knowledge entries, profile management, user management, FAQs, and Machine Learning tools.
- Visible tracking IDs for Reports, Knowledge Entries, and Prompt Templates.
- Search by visible IDs such as `Report #9`, `KB #63`, or `Prompt #4`.
- Knowledge duplicate protection for new entries and report-created knowledge entries.
- Knowledge Entry edit mode locks identity fields and allows safe updates to reusable content, aliases, tags, summary, and published state.
- Published SQL knowledge overrides bundled JSON seed data.
- Feature-context prompt templates ground broad topic answers before normal LLM generation.
- ML.NET reviewer model can classify generated answer quality after training and publishing.
- Rule-based reviewer fallback catches obvious issues even without a published ML.NET model.

## Assistant Knowledge

Assistant behavior comes from two places:

- Bundled JSON seed files under `AIChatApp.Core/Data/Assistants/Documentation`.
- Published SQL rows managed in Backoffice.

The API prefers published SQL data. If no published SQL data exists for a source, it falls back to the bundled JSON files.

Main knowledge types:

- `QuickAnswer`
  - Best for FAQ-style questions with a direct reusable answer.
  - Supports question aliases and tags.
  - Used for fast answers before the normal model is asked.
- `Topic`
  - Best for grouped subject notes and summaries.
  - Useful when the user asks about a broader area.
- `Reference`
  - Best for longer documentation content.
  - Used as supporting context for the assistant.
- `FeatureContext...` prompt templates
  - Best for project-area background truth, such as ML Training, caching, answer matching, or gateway routing.
  - Stored as prompt templates, not Knowledge Entries.
  - Loaded when the user question matches that topic so the LLM can answer broad questions without inventing project behavior.

## Feature Context Prompt Templates

Feature context templates are editable prompt templates that explain a whole project area. They are different from Knowledge Entries:

- Knowledge Entry
  - A specific reusable answer for one question or set of aliases.
  - Example: `What ports does the Gateway use locally?`
- Feature Context
  - Background truth for a feature or workflow.
  - Example: `FeatureContextMLTraining` explains what ML Training does, what files are involved, and what it does not do.

Bundled fallback files live under:

```text
AIChatApp.Core/Data/Assistants/Documentation/Prompts/FeatureContext*.json
```

They are seeded into database-backed Prompt Templates by:

```text
AIChatApp.API/Services/Content/AssistantContentService.cs
SeedPromptTemplatesAsync(...)
```

They are selected and loaded by:

```text
AIChatApp.API/Services/Prompting/PromptBuilder.cs
GetRelevantFeatureContextsAsync(...)
ScoreFeatureContext(...)
```

Current feature context templates:

- `FeatureContextProjectOverview`
- `FeatureContextChatApp`
- `FeatureContextChatOrchestration`
- `FeatureContextPromptBuilding`
- `FeatureContextKnowledgeBase`
- `FeatureContextQuickAnswers`
- `FeatureContextAnswerMatching`
- `FeatureContextBackoffice`
- `FeatureContextReportedResponses`
- `FeatureContextMLTraining`
- `FeatureContextResponseReviewer`
- `FeatureContextCaching`
- `FeatureContextAuthenticationRoles`
- `FeatureContextGatewayRouting`
- `FeatureContextDockerDeployment`
- `FeatureContextConfiguration`
- `FeatureContextLLMModel`
- `FeatureContextFAQContent`
- `FeatureContextTroubleshooting`

## Quick Answer Matching

When a user asks a question, the assistant checks reusable knowledge before using the normal chat model.

User-friendly flow:

```text
User question
  -> exact saved question/alias match
  -> safe close quick-answer match
  -> detect feature/topic
  -> load matching FeatureContext prompt templates
  -> load strongly relevant quick-answer context, if any
  -> load retrieved documentation snippets
  -> normal AI chat answer
  -> response reviewer check
  -> retry/repair if needed
```

What each step means:

- Exact saved question/alias match
  - The user question matches a saved question or alias after basic cleanup.
  - Example: saved alias `What ports does the Gateway use locally?`
- Safe close quick-answer match
  - The words are very similar even if not identical, and the question shape is compatible.
  - Example: `Which local ports does the Gateway use?`
- Question-shape guard
  - Non-exact matches compare the first question word, such as `what`, `where`, `how`, `why`, `when`, or `which`.
  - This helps avoid returning a `what is...` definition for a `where do we use...` question.
- Tags or source support
  - Tags/source can support close wording matches, but weak tag-only matches should not auto-return saved answers.
  - Example tags: `gateway`, `ports`, `local development`, `docker`.
- Feature/topic context
  - If no saved quick answer is safe, `PromptBuilder` detects broad topics and loads up to two matching `FeatureContext...` prompt templates.
  - Example: ML Training terms load `FeatureContextMLTraining`; cache terms load `FeatureContextCaching`.
- Normal AI chat answer
  - The LLM answers using feature context, strongly relevant quick-answer context, retrieved docs/topics/references, answer style rules, chat history, and the current question.

Important logs:

```text
[MATCH:EXACT]
[MATCH:SAFE]
[MATCH:NONE]
[CHAT:LLM]
[CHAT:QUICK-ANSWER]
[REVIEWER:OK]
[REVIEWER:RISK]
Feature prompt context selected ...
Quick-answer prompt context selected ...
```

Backoffice shows this workflow in plain language so admins understand why aliases, tags, and summaries matter.

## Backoffice

Backoffice is the admin workspace for improving the assistant without editing code files directly.

Main sections:

- `Workflow`
  - Shows the improvement loop and current counts.
  - Opens reported responses, prompt templates, knowledge entries, and ML Training.
- `Reported Responses`
  - Lists user-reported answers.
  - Search by `Report #ID`, linked `KB #ID`, prompt, response, user, status, or category.
  - Review modal lets admins set validated question, validated response, review status, category, and notes.
  - Linked knowledge entries can be opened from the report.
- `Prompt Templates`
  - Lists database-backed prompt templates.
  - Search by `Prompt #ID`, template name, profile, content, or status.
  - Edit modal shows `Prompt #ID`.
  - Includes `FeatureContext...` templates used to ground topic-level LLM answers.
- `Knowledge Entries`
  - Lists quick answers, topics, and references.
  - Search by `KB #ID`, title, source, type, summary, content, aliases, or tags.
  - Create and edit modals use the same theme and responsive layout.
  - New entries are checked for duplicate questions and aliases.
- `Machine Learning`
  - Contains Training Data, Training Jobs, and Model Registry.
  - These replaced the separate ML Training web area.
- `Administration`
  - Profile Management and User Management open in modals.
  - User list shows concise user details and opens row details in a modal.

## Report To Knowledge Flow

Recommended flow:

1. User reports a bad or suspicious answer from Chat.
2. Admin opens the report in Backoffice.
3. Admin writes the corrected question and answer.
4. Admin sets review status and category.
5. If the corrected answer should be reused in future chat, admin creates or edits a linked Knowledge Entry.
6. If the Knowledge Entry is published, the chat assistant can use it for future matching.
7. If the report is approved with a category and validated response, it can also become reviewer training data.

Rules:

- A reported response can be reviewed without creating knowledge.
- Creating knowledge makes the corrected answer reusable for future chat.
- Publishing knowledge makes it live for matching.
- Approved reports with a category are training candidates for the reviewer model.
- Duplicate questions and aliases are blocked for new Knowledge Entries.
- Existing linked Knowledge Entries can be updated safely.

## ML Training

ML Training is for the response-quality reviewer. It does not generate the main chat answer.

The normal flow is:

1. The local GGUF model generates an answer.
2. The response reviewer checks the generated answer.
3. The reviewer classifies the answer as `Good`, `Incorrect`, `Incomplete`, `TooLong`, `PromptLeak`, or `Repetitive`.
4. If the answer looks risky, the chat flow can retry or repair the answer.

Training workflow:

1. Admin approves reviewed report examples.
2. Training Data imports approved examples.
3. Training Jobs builds a dataset and trains an ML.NET reviewer model.
4. Model Registry publishes the selected model.
5. The API loads the published `.zip` model if it exists.

Generated model files:

```text
AIChatApp.MLTraining/ReviewerModels/Candidates
AIChatApp.MLTraining/ReviewerModels/published-response-reviewer.zip
```

Important notes:

- Training data teaches the reviewer how to judge answer quality.
- Training data does not directly become a final chat answer.
- To make a corrected answer reusable by Chat, create and publish a Knowledge Entry.
- The reviewer uses ML.NET when a published model exists.
- The reviewer still uses rules when no model exists or when a rule catches an obvious issue.

## Authentication And Roles

Authentication uses JWT. Optional 2FA uses Google Authenticator-compatible TOTP codes.

Backoffice roles:

- `Admin`
  - Full backoffice access.
- `DataValidator`
  - Intended for report review and validation workflows.
- `AppUser`
  - Normal chat user role.

Development includes three clearly labeled local-only accounts so a fresh clone is usable immediately:

- Admin: `localadmin` / `Dummy_Local_Admin123!`
- User: `localuser` / `Dummy_Local_User123!`
- Validator: `localvalidator` / `Dummy_Local_Validator123!`

These accounts are seeded only by the committed Development settings and local Docker configuration. Do not reuse their usernames, emails, or passwords in a deployed environment.

The committed Development settings use obvious dummy credentials. Override them through user secrets when testing real integrations locally:

```powershell
dotnet user-secrets init --project AIChatApp.API
dotnet user-secrets set "JwtSettings:SecretKey" "<long-random-secret>" --project AIChatApp.API
dotnet user-secrets set "ApiKey.Settings:Keys:0" "<gateway-api-key>" --project AIChatApp.API
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<sql-server-connection-string>" --project AIChatApp.API
dotnet user-secrets set "EmailSettings:From" "<sender-address>" --project AIChatApp.API
dotnet user-secrets set "EmailSettings:SmtpServer" "<smtp-host>" --project AIChatApp.API
dotnet user-secrets set "EmailSettings:Username" "<smtp-username>" --project AIChatApp.API
dotnet user-secrets set "EmailSettings:AppPassword" "<smtp-app-password>" --project AIChatApp.API

dotnet user-secrets init --project AIChatApp.Gateway
dotnet user-secrets set "ApiKey.Settings:Keys:0" "<gateway-api-key>" --project AIChatApp.Gateway

dotnet user-secrets init --project AIChatApp.Web
dotnet user-secrets set "Frontend:ApiKey" "<gateway-api-key>" --project AIChatApp.Web
```

Do not store personal usernames, real emails, real passwords, app passwords, or production keys in this README.

## Configuration

Important settings:

- `AssistantProfile:ProfileId`
  - Selects the active assistant profile.
- `AssistantProfile:AssistantName`
  - Sets the assistant label.
- `LocalModel:FileName`
  - Selects the GGUF model file under `AIChatApp.Core/Models`.
- `LocalModel:ContextSize`
  - Sets the model context window.
- `ResponseReviewer:Enabled`
  - Enables or disables reviewer checks.
- `ResponseReviewer:PublishedModelPath`
  - Points to the published reviewer `.zip`.
- `ResponseReviewer:CandidateModelFolder`
  - Stores trained candidate reviewer models.
- `Frontend:GatewayBaseUrl`
  - Tells the Web app which Gateway URL to call.
- `ApiKey.Settings`
  - Controls gateway client name and API keys.
- `JwtSettings`
  - Controls JWT issuer, audience, and signing key.
- `ConnectionStrings`
  - Controls SQL Server connections.

Sensitive values should be supplied through user secrets locally and environment variables outside local development.

## Local Model Setup

Download a compatible GGUF model, for example:

- `qwen2.5-3b-instruct-q4_k_m.gguf`

Place the model here:

```text
AIChatApp.Core/Models
```

Then point `LocalModel:FileName` to the file name.

## Local Development

Apply database migrations manually if needed:

```powershell
dotnet ef database update --project AIChatApp.API --startup-project AIChatApp.API
```

Run the API:

```powershell
dotnet run --project AIChatApp.API
```

Run the Gateway:

```powershell
dotnet run --project AIChatApp.Gateway
```

Run the Web app:

```powershell
dotnet run --project AIChatApp.Web
```

Run the console client:

```powershell
dotnet run --project AIChatApp.Console
```

Common local routes:

- Web Chat: `/chat`
- Web Backoffice: `/backoffice`
- Web FAQs: `/faqs`
- Backoffice Machine Learning: `/backoffice` > Machine Learning

Common development ports may vary by launch profile. Check each project's `launchSettings.json` or the terminal output after startup.

## Docker Compose

The Compose stack runs SQL Server, API, Gateway, and Web containers. It contains obvious local-only dummy credentials and seeded test accounts so it can start without creating an `.env` file.

Start the local stack:

```powershell
docker compose config --quiet
docker compose up --build
```

To override any local default, create a gitignored environment file from the committed template and edit the desired values:

```powershell
Copy-Item .env.example .env
```

The API, Gateway, and Web containers must use the same `API_KEY`. Keep the selected GGUF file in `AIChatApp.Core/Models`; Compose mounts that folder read-only instead of copying model files into an image. The dummy SMTP settings allow startup but do not send email until replaced with a working local test server or real settings supplied outside source control.

Open the Web app at `http://localhost:44318`. The Gateway is available at `http://localhost:5001`, the API at `http://localhost:5157`, and SQL Server at `localhost,14333`. Published ports are bound to `127.0.0.1`, so this local stack is not exposed to other network hosts.

Stop the containers without deleting the SQL data volume:

```powershell
docker compose down
```

Default local Backoffice account seeding is enabled in Docker. For production, disable seeding, replace every dummy value through the deployment platform's secret manager, and terminate TLS at a trusted reverse proxy. Production startup rejects missing or placeholder API, JWT, database, and email settings.

## Gateway

Requests through the Gateway require API key headers:

```http
X-Api-Client: <client-name>
X-Api-Key: <api-key>
```

Authenticated requests also require:

```http
Authorization: Bearer <jwt-token>
```

Gateway route overview:

- `/auth/*` forwards to API auth endpoints.
- `/chat/*` forwards to API chat endpoints.
- `/chathistory/*` forwards to API chat history endpoints.

For local Web development, use the HTTPS Gateway URL when possible so authenticated requests do not lose the `Authorization` header during redirects.

## Main API Endpoints

Auth:

- `POST /api/auth/register`
- `POST /api/auth/login`
- `POST /api/auth/2fa/setup`
- `POST /api/auth/2fa/verify`
- `POST /api/auth/2fa/disable`
- `POST /api/auth/reset-password`

Chat:

- `POST /api/chat/ask-stream`
- `POST /api/chat/ask-ai`
- `POST /api/chat/ask-continue`

Chat history:

- `GET /api/chathistory/conversations/{chatId}`

Backoffice:

- workflow summary
- reported responses
- prompt templates
- knowledge entries
- user management
- reviewer training data
- reviewer training jobs
- reviewer model registry

## Example Chat Request

Streaming request:

```json
{
  "chatId": "demo-chat",
  "user": "demo-user",
  "prompt": "What ports does the Gateway use locally?",
  "contextMode": "documentation"
}
```

`ask-stream` returns Server-Sent Events with streamed token chunks and a final completion event.

## FAQ Behavior

The FAQ experience reads quick-answer style documentation content. Published SQL knowledge can replace the bundled JSON seed data, so Backoffice updates can become visible without changing source files.

## Testing

Run tests:

```powershell
dotnet test AIChatApp.Tests/AIChatApp.UnitTesting.csproj
```

Build the Web app without writing into locked `bin` folders:

```powershell
dotnet build AIChatApp.Web/AIChatApp.Web.csproj -p:UseAppHost=false -o artifacts/build/web
```

## Security Notes

- Keep real credentials out of source control and documentation.
- Keep committed credentials unmistakably dummy and restricted to Development or local Docker environments.
- Use user secrets for local sensitive values.
- Use a gitignored `.env` file for local Docker Compose and a secret manager for deployed environments.
- Docker build context excludes `.env`, local development settings, logs, build output, and model files.
- Never pass secrets as Docker build arguments because image metadata can retain them.
- Rotate any secret that was ever committed accidentally.
- Do not expose personal usernames, real emails, passwords, API keys, app passwords, or JWT secrets in README examples.
