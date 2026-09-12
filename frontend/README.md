# Falaq Food Call Center — Angular Agent Dashboard Phase

## Phase
Agent Dashboard

## Scope
Implemented only the Agent Dashboard on top of the completed Angular frontend foundation.

### Dashboard features
- Agent name and employee code
- Current agent status
- Status controls: Available, Busy, Offline, WrapUp
- Current call
- Active queue count
- Five most recent calls
- Loading and error states with retry
- Backend API integration
- SignalR-driven refresh for agent status, call status, queue updates and incoming calls
- Responsive enterprise-style layout

## Backend contract mismatch discovered
The existing MVP API did not expose an authenticated Agent-only dashboard endpoint. Agents also could not call `GET /api/v1/agents/{id}` because that endpoint is restricted to Admin/Supervisor, and there was no queue-count endpoint.

A minimal contract extension was therefore added:

`GET /api/v1/agents/me/dashboard`

This returns the authenticated agent's identity/status, current call, active queue count and recent calls.

No database migration is required.

The backend also keeps the existing status endpoint:

`PUT /api/v1/agents/{agentId}/status`

with numeric enum values matching the existing ASP.NET Core JSON contract.

## Run frontend
```bash
npm install
npm start
```

Frontend: `http://localhost:4200`

Backend API base: `http://localhost:5289/api/v1`

## Verify
```bash
npm run build
```

Backend verification:
```bash
dotnet restore
dotnet build CallCenter.sln
dotnet test CallCenter.sln
```

The generated environment did not have a working .NET SDK/npm dependency installation available long enough to claim a successful build/test run, so these commands must be run in the user's development environment.

## Phase gate
Stop here. Do not implement Customers, Incoming Call, Active Call, Call History, Reports, Agent Management or Settings in this phase.
