# AI Lab — Think Postman for AI Prompts and Workflows

**A developer workbench for designing, testing, comparing, and orchestrating AI calls across multiple models and providers.**

AI Lab brings the repeatability and structured experimentation of tools like Postman to AI application development.

Instead of testing prompts manually across different provider consoles, AI Lab provides a single local environment where developers can define AI requests, vary prompts, models and settings, compare results, benchmark performance, and compose individual requests into multi-step AI workflows.

The goal is simple: make AI behavior **observable, comparable, reproducible, and easier to engineer**.

## Why AI Lab?

Building an AI-enabled application involves much more than finding a prompt that appears to work once.

Developers routinely need to answer questions such as:

- Does the same prompt behave differently across OpenAI, Claude, Gemini, or Grok?
- Is a more expensive model actually producing a meaningfully better result?
- How much does reasoning level affect latency, token usage, and cost?
- How consistent is a request across repeated executions?
- What happens when the output of one AI call becomes context for another?
- Which parts of a workflow can execute in parallel?
- What happens when one request in a multi-step workflow fails?
- How much time is spent waiting for the first token versus generating the response?
- How can an experiment be reproduced after prompts, settings, or models change?

AI Lab provides an engineering environment for exploring those questions systematically.

## Core Capabilities

### Multi-provider AI execution

AI Lab provides a common execution model across multiple AI providers:

- OpenAI
- Anthropic / Claude
- Google Gemini
- xAI / Grok

Provider-specific implementations are isolated behind a common abstraction while still allowing provider-specific capabilities and settings to be preserved.

This makes it possible to compare different models without rewriting the surrounding application workflow.

### Prompt and model experimentation

Each AI request can independently define its:

- provider
- model
- system prompt
- user context
- cached context
- reasoning configuration
- temperature
- output-token limits
- stop sequences
- structured-output schema
- provider-specific settings
- attachments

Requests can be saved and rerun, allowing changes to prompts, models, or parameters to be evaluated against consistent inputs.

### Side-by-side model comparison

AI Lab captures execution data that makes model and prompt comparisons measurable rather than subjective.

Depending on provider capabilities, captured metrics include:

- total execution time
- time to first output token
- generation time
- input tokens
- cached input tokens
- output tokens
- reasoning tokens
- cache-hit percentage
- output tokens per second
- estimated execution cost
- execution status
- actual model used

Outputs can also be compared using structured JSON diffing or text diffing.

### Benchmarking

A request can be executed repeatedly to measure consistency and performance across multiple runs.

Benchmark statistics make it possible to evaluate characteristics such as latency, token usage, cost, and output behavior rather than relying on a single successful execution.

Execution history is persisted so previous runs remain available for analysis.

## AI Workflow Orchestration

Individual AI calls can be composed into **Execution Plans**.

An Execution Plan is represented as a directed acyclic graph (DAG) in which nodes are AI requests and edges define dependencies between them.

This allows AI Lab to model workflows such as:

```text
                 ┌──> Analyze Financial Data ──┐
Input ───────────┤                              ├──> Produce Final Report
                 └──> Analyze Documents ───────┘
```

Independent nodes execute concurrently.

A downstream node begins as soon as **its actual dependencies** have completed; it does not wait for unrelated nodes at the same logical level.

Outputs from upstream requests can become inputs to downstream requests through bindings, allowing complex AI pipelines to be assembled from small reusable requests.

Examples include:

```text
Extract Data
    ↓
Classify
    ↓
Evaluate
    ↓
Generate Recommendation
```

or:

```text
             ┌──> Model A ──┐
Input ───────┼──> Model B ──┼──> Compare / Synthesize
             └──> Model C ──┘
```

The same request definition can also participate in multiple nodes within a plan, with each node maintaining its own execution identity and dependencies.

## Failure Handling

AI workflows need explicit failure semantics rather than assuming every model call succeeds.

Execution Plans support configurable failure behavior, including:

- retry
- exponential backoff
- fail the plan
- continue with an error

Concurrency is controlled both globally and per provider, preventing an execution plan from overwhelming a provider while still allowing unrelated work to proceed in parallel.

Cancellation propagates through the execution plan and individual execution states are preserved.

## Observability and Reproducibility

Every execution creates an `ExecutionRun` containing both the result and the context required to understand how that result was produced.

The execution snapshot records information such as:

- provider and requested model
- resolved prompts and context
- resolved variable bindings
- attachment hashes
- reasoning configuration
- generation parameters
- structured-output configuration
- provider settings
- timestamps
- token usage
- cost estimates
- raw provider response
- normalized provider request

Streaming events are tracked separately so AI Lab can distinguish:

```text
Request started
      ↓
First provider response
      ↓
First output token
      ↓
Streaming output
      ↓
Completed
```

This makes latency behavior visible rather than reducing every request to a single elapsed-time number.

## Input and Output Binding

AI requests can consume:

- workspace variables
- user context
- cached context
- attached files
- outputs from previous workflow nodes

Bindings are resolved at execution time.

Within an Execution Plan, upstream results are made available to dependent nodes, including JSON-aware access when the upstream response contains structured data.

This allows requests to remain reusable while the execution graph determines how information flows between them.

## Structured Output

AI Lab supports structured-output scenarios where provider capabilities allow them.

Because providers implement structured output and reasoning differently, the provider layer handles those differences rather than pretending every API behaves identically.

For example, reasoning configuration, JSON-schema output, caching, and token accounting can differ significantly between providers.

AI Lab preserves those differences while exposing a consistent application-level execution model.

## Architecture

AI Lab is a local full-stack application.

```text
┌───────────────────────────────────────┐
│              Angular UI               │
│                                       │
│ Requests • Models • Runs • Plans      │
│ Comparison • Metrics • Workspaces     │
└───────────────────┬───────────────────┘
                    │ REST / Streaming
                    ▼
┌───────────────────────────────────────┐
│            ASP.NET Core API           │
│                                       │
│ Execution • Persistence • Catalogs    │
│ Benchmarking • Comparison • Plans     │
└───────────────────┬───────────────────┘
                    │
                    ▼
┌───────────────────────────────────────┐
│             AI Lab Core               │
│                                       │
│ Request Executor                      │
│ Execution Plan Engine                 │
│ Binding Resolution                    │
│ Statistics / Cost Calculation         │
└───────┬────────┬────────┬─────────────┘
        │        │        │
        ▼        ▼        ▼
     OpenAI   Claude   Gemini   Grok
```

The current application uses:

- **.NET 10 / ASP.NET Core** for the backend
- **Angular** for the UI
- **SQLite / Entity Framework Core** for local persistence
- provider-specific HTTP integrations for AI execution

The core execution layer is kept independent from ASP.NET Core and persistence concerns so orchestration and request execution logic can be tested independently.

## Local-first Security Model

AI Lab is designed as a developer tool and runs locally.

Your AI provider credentials remain under your control rather than being sent to an AI Lab-hosted service.

Credentials can be configured from the command line (see [Configure provider credentials](#configure-provider-credentials) below), or interactively when the backend starts, if you haven't configured them yet.

The browser UI does not need to directly own provider API credentials. AI calls are executed through the local .NET backend.

This also makes AI Lab useful for experimenting with proprietary prompts or data without introducing an additional hosted intermediary.

## Testing

The project includes automated tests around important execution behavior, including:

- AI request execution
- execution-plan graph behavior
- execution-plan orchestration
- input binding resolution
- provider request construction
- provider response parsing
- structured-output/schema handling
- token accounting
- cost calculation
- descriptive statistics
- output diffing

Provider implementations can be exercised through test doubles without requiring every test to make a live external AI request.

Run the suite with:

```bash
dotnet test tests/AiLab.Tests
```

## Repository Structure

```text
src/
├── AiLab.Api
├── AiLab.Core
├── AiLab.Infrastructure
└── AiLab.UI

tests/
└── AiLab.Tests
```

## Running Locally

### Prerequisites

You will need:

- **.NET 10 SDK**
- **Node.js / npm** (a current LTS Node release; see `src/AiLab.UI/package.json` for the Angular version in use)
- API credentials for whichever AI providers you want to use

You do not need credentials for every supported provider.

### Configure provider credentials

AI Lab resolves each provider's API key in this order: a real OS environment variable first, then a `.env` file in the app's **data directory** — `src/AiLab.Api/data/.env` by default, or `$AILAB_DATA_DIR/.env` if you override the data directory. That file is created for you and is git-ignored; a `.env` at the repository root is **not** read by the app.

The easiest way to set keys is the interactive configure command, which writes directly to that data-directory `.env` file (input is masked, and you can leave any provider blank to skip it):

```bash
dotnet run --project src/AiLab.Api -- configure
```

You can also edit the data-directory `.env` file by hand, or export the variables in your shell. The recognized names, one per provider:

| Provider  | Environment variable(s)                 |
|-----------|------------------------------------------|
| OpenAI    | `OPENAI_API_KEY`                          |
| Anthropic | `ANTHROPIC_API_KEY`                       |
| Grok / xAI| `XAI_API_KEY` (or `GROK_API_KEY`)         |
| Gemini    | `GEMINI_API_KEY` (or `GOOGLE_API_KEY`)    |

The root-level [`.env.example`](.env.example) lists these same variable names for reference — copy the ones you need into the data-directory `.env` file above rather than into a root `.env`.

### Run the backend

From the repository root:

```bash
dotnet run --project src/AiLab.Api
```

This applies any pending database migrations, then listens on `http://127.0.0.1:8765` by default. The port can be overridden using `AILAB_PORT` or the `--port` command-line option.

### Run the UI in development

The backend must already be running (previous step) — the dev server proxies API calls to it.

```bash
cd src/AiLab.UI
npm install
npm start
```

This starts the Angular dev server, which rebuilds automatically on every save:

```text
http://localhost:4200
```

Requests to `/api/*` are proxied to the backend at `http://127.0.0.1:8765` (see `src/AiLab.UI/proxy.conf.json`). If you run the backend on a different port via `AILAB_PORT`/`--port`, update that file's target to match, or the UI will load but API calls will fail.

### Build the UI for the backend to serve

The backend can also serve the Angular app directly from its own port, but only from a **pre-built static copy** in `src/AiLab.Api/wwwroot` — it does not proxy to the dev server and won't pick up UI changes on its own. Build (or rebuild) that copy with:

```bash
cd src/AiLab.UI
npm run build
```

This runs `ng build`, which outputs straight into `src/AiLab.Api/wwwroot`. After building, running `dotnet run --project src/AiLab.Api` alone serves the full app — API and UI together — from the backend's own port. Re-run this build any time `src/AiLab.UI` changes, or the backend keeps serving a stale snapshot.

## Example Use Cases

### Compare models

Create several requests using the same input and prompt but different providers or models.

Run them and compare:

```text
Claude
GPT
Gemini
Grok
```

across output quality, latency, token usage, reasoning behavior, and estimated cost.

### Optimize a prompt

Keep the model and input constant while testing multiple prompt variations.

Compare outputs and execution statistics to determine whether a prompt change actually improves the result.

### Evaluate model economics

Run the same task against multiple models and compare quality with latency and estimated cost.

A larger model is not automatically the right model for every step of an application.

### Prototype an AI pipeline

Build several specialized requests and connect them into an Execution Plan:

```text
Extract
   ↓
Normalize
   ↓
       ┌──> Analyze A ──┐
       │                │
       └──> Analyze B ──┼──> Synthesize
                        │
       └──> Analyze C ──┘
```

AI Lab determines dependencies, executes independent requests concurrently, propagates outputs, collects execution statistics, and applies configured failure policies.

## Development with AI

AI Lab is itself an example of AI-assisted software engineering.

The application has been developed extensively with **Claude Code** as a repository-level engineering tool rather than simply as a code-completion assistant.

AI-assisted development has been used across:

- architecture analysis
- implementation across frontend and backend layers
- debugging and root-cause analysis
- refactoring
- test creation
- provider integration
- workflow/orchestration implementation
- code review
- documentation
- verification of changes across the repository

Architectural decisions, requirements, validation, and final engineering responsibility remain human-controlled.

This development model is also one of the motivations behind AI Lab: as AI becomes part of the engineering workflow, developers need better tools for evaluating the AI components they put into applications.

## Project Status

AI Lab is under active development.

The current focus is on the engineering foundation for repeatable AI experimentation and workflow execution. Future capabilities may expand evaluation, visualization, model/provider support, workflow tooling, and automated analysis of experiment results.

The repository reflects the actual implementation as it evolves; roadmap ideas are intentionally kept separate from currently implemented functionality.

## Author

**Tigran Aleksanyan**  
Founder & Software Architect — Ingenious Software Solutions

Portfolio: https://ingenioussoftwaresolutions.com/projects  
LinkedIn: https://www.linkedin.com/in/tgralex/

## License

AI Lab is open-source software licensed under the [MIT License](LICENSE).

Copyright © 2026 Tigran Aleksanyan.

---

**AI Lab — experiment with AI like an engineer, not a chat session.**
