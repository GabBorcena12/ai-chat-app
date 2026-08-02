# AIChatApp

AIChatApp is a local .NET 10 documentation assistant. It combines a Blazor chat interface, reusable project knowledge, prompt grounding, local Qwen GGUF inference through LLamaSharp, reported-response review, and an ML.NET response-quality reviewer.

## Features

- Local `qwen2.5-3b-instruct-q4_k_m.gguf` inference through LLamaSharp.
- Streaming chat responses through Server-Sent Events.
- Non-streaming responses and continuation for incomplete answers.
- Exact and safe-close matching for reusable quick answers.
- Prompt grounding with system context, feature context, retrieved documentation, answer rules, and chat history.
- SQL-backed prompt templates and knowledge entries with bundled JSON fallback content.
- Browser-persisted conversations with rename, delete, copy, read-aloud, report, and continue actions.
- Reported-response review with corrected questions, corrected answers, categories, notes, and linked knowledge.
- Knowledge types for quick answers, topics, and longer references.
- Duplicate protection for knowledge titles and aliases.
- ML.NET reviewer training, candidate model creation, model publishing, and runtime model reload.
- Rule-based response review when no published ML.NET model exists or an obvious problem is detected.

## Project Structure And Contents

```text
AIChatApp
|-- AIChatApp.Web
|   |-- Components/Pages
|   |-- Models
|   `-- Services
|-- AIChatApp.Gateway
|-- AIChatApp.API
|   |-- Controllers/ChatApp
|   |-- Controllers/Backoffice
|   |-- Models/ChatApp
|   `-- Services
|       |-- ChatApp
|       |   |-- Content
|       |   |-- History
|       |   |-- LLM
|       |   |-- Orchestration
|       |   |-- Processing
|       |   `-- Prompting
|       `-- Backoffice
|-- AIChatApp.Core
|   |-- Config
|   |-- Data/Assistants/Documentation
|   |   |-- Knowledge
|   |   `-- Prompts
|   |-- Data_Context
|   |-- Models
|   `-- Services
|-- AIChatApp.MLTraining
|   |-- Data
|   |-- Models
|   |-- ReviewerModels
|   `-- Services
`-- AIChatApp.Tests
```

### Project Responsibilities

| Project | Contents and responsibility |
|---|---|
| `AIChatApp.Web` | Blazor pages, Chat and Backoffice UI, browser conversation state, and the Gateway client. |
| `AIChatApp.Gateway` | YARP entry point that forwards Chat, history, and Backoffice requests to the API. |
| `AIChatApp.API` | Chat endpoints, orchestration, prompt construction, knowledge matching, local LLM access, reporting, persistence, and Backoffice workflows. |
| `AIChatApp.Core` | Shared configuration, entities, database context, content paths, assistant models, and bundled documentation content. |
| `AIChatApp.MLTraining` | Training workspace, ML.NET reviewer trainer, runtime reviewer, model metadata, seed examples, and generated reviewer models. |
| `AIChatApp.Tests` | Chat response-processing, knowledge-content, training-workflow, and reviewer-quality tests. |

### Important Content

```text
AIChatApp.Core/Data/Assistants/Documentation/Knowledge
|-- Architecture.json
|-- Auth.json
|-- ChatEndpoints.json
|-- ConfigReference.json
|-- Docker.json
|-- Faq.json
|-- ModelReference.json
|-- QuickAnswers.json
`-- Troubleshooting.json

AIChatApp.Core/Data/Assistants/Documentation/Prompts
|-- SystemContext.json
|-- AnswerStyle.json
|-- ContinuationTemplate.json
|-- RetryTemplate.json
`-- FeatureContext*.json
```

## End-To-End Workflow

```text
User submits a question in Chat
  -> Home.razor sends the request through AIChatGatewayClient
  -> Gateway forwards the request to ChatController
  -> ChatController calls ChatOrchestrator
  -> ChatOrchestrator saves the user message
  -> quick-answer matching checks published reusable knowledge
     -> exact or safe match: save and return the reusable answer
     -> no safe match: continue to prompt construction
  -> PromptBuilder builds a grounded prompt
  -> LlamaLLMService runs the local Qwen GGUF model through LLamaSharp
  -> AgentResponseProcessor cleans the generated text
  -> ResponseReviewerService checks answer quality
     -> acceptable: keep the answer
     -> risky or incomplete: retry or repair when allowed
  -> ChatOrchestrator saves the final assistant message
  -> ChatController returns JSON or streams completion chunks
  -> Chat displays and stores the conversation
```

Main implementation points:

| Step | Class or method |
|---|---|
| Receive streaming request | `ChatController.AskStream(...)` |
| Receive normal request | `ChatController.AskAi(...)` |
| Continue an answer | `ChatController.AskContinue(...)` |
| Coordinate chat | `ChatOrchestrator.StreamAsync(...)`, `AskAsync(...)`, and `ContinueAsync(...)` |
| Match reusable answers | `ChatOrchestrator.TryGetFastDocumentationAnswerAsync(...)` |
| Build grounded prompt | `PromptBuilder.BuildPromptAsync(...)` |
| Generate model tokens | `LlamaLLMService.GenerateAsync(...)` |
| Clean model output | `AgentResponseProcessor` |
| Review answer quality | `ResponseReviewerService.Review(...)` |
| Store conversation | `ChatHistoryService` and the chat message entities |

## Prompt System

`PromptBuilder.BuildPromptAsync(...)` creates the final model input in this order:

1. Load `SystemContext.json`.
2. Classify the documentation intent.
3. Select up to two relevant `FeatureContext...` prompt templates.
4. Add strongly relevant quick-answer context.
5. Add retrieved topic and reference documentation.
6. Add `AnswerStyle.json` and a question-specific answer-length rule.
7. Add recent conversation history.
8. Add the current user message and assistant prefix.

Feature-context templates describe broad areas such as Chat orchestration, prompt building, knowledge matching, reported responses, ML training, response review, caching, and troubleshooting. They ground normal model generation but are not returned directly as saved answers.

Prompt sources follow the same preference as knowledge content:

```text
Published SQL prompt template
  -> used when available
  -> otherwise use the matching bundled JSON prompt
```

The continuation and repair paths use `ContinuationTemplate.json` and `RetryTemplate.json`. Prompt construction stays outside `LlamaLLMService`; the LLM service only converts a completed prompt into streamed model tokens.

## Reported Responses

Users can report an answer directly from Chat. `ChatController.ReportResponse(...)` stores the question, generated response, user, chat reference, and initial review state.

Review workflow:

1. A user reports an incorrect, incomplete, repetitive, overly long, or suspicious answer.
2. Backoffice loads the report through `BackofficeReportService`.
3. A reviewer enters a validated question and validated response.
4. The reviewer sets the status, issue category, and notes.
5. The reviewer may promote the correction to a linked Knowledge Entry.
6. Published linked knowledge becomes available to future Chat requests.
7. Approved categorized reports become candidates for ML reviewer training.

A report and a Knowledge Entry have different purposes:

| Record | Purpose |
|---|---|
| Reported response | Preserves the original problem, review decision, correction, category, reviewer, and audit trail. |
| Knowledge Entry | Makes an approved answer reusable by future Chat requests. |
| Training example | Teaches the ML.NET reviewer to classify answer quality; it does not become a chat answer. |

## Knowledge Base

Knowledge is provided by published SQL records and bundled JSON files under `AIChatApp.Core/Data/Assistants/Documentation/Knowledge`.

```text
Request knowledge for the Documentation profile
  -> query published SQL entries
  -> if published entries exist, use them
  -> otherwise load bundled JSON content
  -> cache the result for repeated requests
```

`AssistantContentService` owns this loading and fallback behavior. Backoffice writes through `BackofficeContentService`, which invalidates the affected profile cache after prompt or knowledge changes.

Knowledge types:

| Type | Use |
|---|---|
| `QuickAnswer` | A direct reusable answer with a title, aliases, keywords, source, summary, and answer content. |
| `Topic` | Grouped information for broader subject questions. |
| `Reference` | Longer supporting documentation used during prompt retrieval. |

Quick-answer matching follows this order:

1. Normalize the user question and saved titles or aliases.
2. Return an exact match immediately.
3. Evaluate safe close matches using wording similarity and question shape.
4. Use aliases, keywords, and source information as supporting signals.
5. Avoid weak tag-only matches.
6. When no match is safe, pass relevant knowledge into the normal prompt instead of returning it directly.

New Knowledge Entries are checked for duplicate normalized titles and aliases. Published updates become visible after cache invalidation without changing source JSON files.

## ML Training

ML Training builds a response-quality reviewer. It does not generate the main chat answer and does not replace the local Qwen model.

Training data comes from:

- Approved reported responses with an issue category, used as problem examples.
- Published Knowledge Entries, used as `Good` answer examples.
- Bundled reviewer seed examples in `AIChatApp.MLTraining/Data/ReviewerTrainingExamples.json`.

Training workflow:

```text
Approved reports and published knowledge
  -> BackofficeReviewerService.BuildDatasetAsync()
  -> TrainingWorkspaceService.BuildDataset(...)
  -> BackofficeReviewerService.TrainAsync(...)
  -> ResponseReviewerTrainer.TrainAndSave(...)
  -> candidate model ZIP
  -> BackofficeReviewerService.PublishLatest()
  -> published-response-reviewer.zip
  -> ResponseReviewerService.ReloadModel()
```

`ResponseReviewerTrainer` uses ML.NET text featurization and multiclass classification. Labels include `Good`, `Incorrect`, `Incomplete`, `TooLong`, `PromptLeak`, and `Repetitive`.

Generated models are stored under:

```text
AIChatApp.MLTraining/ReviewerModels/Candidates
AIChatApp.MLTraining/ReviewerModels/published-response-reviewer.zip
```

At runtime, `ResponseReviewerService` uses the published model when available and combines it with rule-based checks. A risky result can trigger one controlled repair attempt in `ChatOrchestrator`.

## Chat App

The Blazor Chat experience is implemented primarily in `AIChatApp.Web/Components/Pages/Home.razor` and communicates through `AIChatGatewayClient`.

Chat supports:

- Starting and reopening browser-persisted conversations.
- Streaming answer text as it is generated.
- Loading saved server-side conversation messages.
- Renaming and deleting conversations.
- Copying and reading answers aloud.
- Reporting an answer for review.
- Continuing an incomplete answer.
- Showing completion and error states without replacing saved conversation content.

The main generation model is Qwen 2.5 3B Instruct from `qwen2.5-3b-instruct-q4_k_m.gguf`. LLamaSharp is the .NET runtime adapter around `llama.cpp`; it loads the GGUF model and exposes local token generation to `LlamaLLMService`.

Chat uses two answer paths:

```text
Fast path
  Published or bundled QuickAnswer
  -> exact or safe reusable match
  -> immediate saved answer

Generated path
  System context + feature context + knowledge + history + question
  -> local Qwen generation
  -> cleanup and reviewer check
  -> optional repair
  -> saved and streamed final answer
```
