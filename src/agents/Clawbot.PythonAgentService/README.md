# ClawBot Python AgentService

Contract-compatible Python alternative for the .NET `Clawbot.AgentService` gRPC host.

This service compiles the repository `proto/*.proto` files at startup and registers default gRPC servicers for every current AgentService contract. It is intended as a deployable Python host skeleton for AI-heavy implementations while keeping the API-side `AgentService:Url` contract unchanged.

## Status: placeholder, not wired

This is a **reference scaffold**, not a running deployment target:

- Not referenced by any C# project — the .NET `Clawbot.AgentService` is the live implementation behind `AgentService:Url`.
- Not started by `run-all.bat` or any other local/CI run path.
- RPC method bodies return default messages only (see Implementation Notes below); no AI pipeline logic is ported here.

It exists to prove the proto contracts stay Python-compatible, in case a future Python rewrite of `Clawbot.AgentService` is undertaken. Structure is covered by `PythonAgentServiceScaffoldTests`; treat any change here as scaffold maintenance, not feature work.

## Covered gRPC Services

- `Orchestrator`
- `ChatAgent`
- `ContentAgent`
- `LeadAgent`
- `SaleAssistAgent`
- `DocsAgent`
- `AdsAgent`
- `ReportAgent`
- `ResearchAgent`

## Run Locally

```powershell
cd src/agents/Clawbot.PythonAgentService
python -m venv .venv
.venv\Scripts\pip install -r requirements.txt
$env:CLAWBOT_PROTO_ROOT = "..\..\..\proto"
$env:CLAWBOT_AGENT_PORT = "5050"
.venv\Scripts\python -m app.main
```

Point the API to this process with:

```text
AgentService__Url=http://localhost:15875
```

## Container

Build from the repository root so the Dockerfile can copy the shared proto directory:

```powershell
docker build -f src/agents/Clawbot.PythonAgentService/Dockerfile -t clawbot-python-agent .
docker run --rm -p 15875:5050 clawbot-python-agent
```

## Implementation Notes

- The host dynamically compiles `proto/*.proto` via `grpcio-tools`.
- Unary RPCs return default response messages; streaming RPCs yield one default message.
- gRPC health is registered for the server and each package-qualified service.
- Replace individual method bodies in `app/main.py` or route them to Python AI pipelines as those implementations are ported.
