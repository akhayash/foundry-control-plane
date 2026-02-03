# A2AAgent

A minimal A2A-only ASP.NET Core host that returns chat completions from Azure OpenAI. It exposes:

- A2A endpoint at `/a2a`
- Agent card at `/.well-known/agent-card.json`
- Health probe at `/healthz`

## Configure

1. Copy `appsettings.Development.json.example` to `appsettings.Development.json` (gitignored).
2. Set:
   - `AzureOpenAI:Endpoint`
   - `AzureOpenAI:DeploymentName`
   - Optional: `ApplicationInsights:ConnectionString` for telemetry.
3. Ensure `az login` is done; the app uses `AzureCliCredential`.

## Run locally

```bash
cd src/A2AAgent
dotnet run
```

```bash
curl http://localhost:5230/.well-known/agent-card.json
```

For messaging, prefer the `A2AClient` from the `A2A` package or the A2A CLI samples (`samples/AgentClient` in the upstream repo) pointing at `http://localhost:5230/a2a`.

## Notes

- Only A2A protocol is implemented; no Responses/Assistants surface.
- Telemetry is disabled unless `ApplicationInsights:ConnectionString` is provided.
- Public base URL in `Agent:PublicBaseUrl` is used to stamp the agent card for Foundry registration.
