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

Credentials can be configured from the command line:

```bash
ailab configure
```

or when the backend starts.

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

## Repository Structure

```text
src/
├── AiLab.Api
├── AiLab.Core
├── AiLab.Infrastructure
└── AiLab.UI

tests/
└── AiLab.Tests

WinForms/
└── Earlier desktop prototype
```

The repository also contains the earlier WinForms prototype from which the current full-stack implementation evolved.

## Running Locally

### Prerequisites

You will need:

- .NET SDK
- Node.js / npm
- API credentials for whichever AI providers you want to use

You do not need credentials for every supported provider.

### Configure provider credentials

Configure your API keys locally using the backend configuration command.

For example:

```bash
dotnet run --project src/AiLab.Api -- configure
```

Provider credentials are stored locally and are intentionally excluded from source control.

### Run the Angular development server

```bash
cd src/AiLab.UI
npm install
npm start
```

The development UI is available at:

```text
http://localhost:4200
```

### Run the backend

From the repository root:

```bash
dotnet run --project src/AiLab.Api
```

By default, the backend runs locally and can also serve the compiled Angular application.

The port can be overridden using `AILAB_PORT` or the `--port` command-line option.

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

---

**AI Lab — experiment with AI like an engineer, not a chat session.**
