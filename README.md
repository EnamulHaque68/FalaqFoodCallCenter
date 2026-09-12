# 🍽️ Falaq Food Call Center — Enterprise Cloud Contact Center & Telephony Platform

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat&logo=dotnet)](https://dotnet.microsoft.com/)
[![Angular](https://img.shields.io/badge/Angular-19-DD0031?style=flat&logo=angular)](https://angular.dev/)
[![Entity Framework Core](https://img.shields.io/badge/EF%20Core-8.0-512BD4?style=flat)](https://learn.microsoft.com/ef/core/)
[![SignalR](https://img.shields.io/badge/SignalR-Real--Time-blue?style=flat)](https://dotnet.microsoft.com/apps/aspnet/signalr)
[![Twilio Voice](https://img.shields.io/badge/Telephony-Twilio%20%26%20Simulated-F22F46?style=flat&logo=twilio)](https://www.twilio.com/)
[![Tests](https://img.shields.io/badge/Tests-270%2B%20Passing-success?style=flat)]()

**Falaq Food Call Center** is an enterprise-grade, omnichannel contact center and customer relationship management (CRM) platform built with **.NET 8 Clean Architecture** and **Angular 19**. Engineered for high-throughput food delivery, customer support operations, real-time agent dispatch, telephony lifecycle management, and detailed analytical reporting.

---

## 🌟 Key Highlights & Capabilities

### 📞 1. Real-Time Telephony & Softphone
- **Global Softphone Dialpad:** Interactive 12-key DTMF dialer with browser audio tones, outbound dial, test ringing, and quick customer lookup.
- **Pluggable Telephony Engine:** Production-ready dual provider pattern:
  - **Twilio Voice Integration:** TwiML generation, WebRTC client tokens, secure webhook signature verification, and call recording lifecycle.
  - **Deterministic Simulated Provider:** High-fidelity simulation mode for development and automated testing without telecom costs.
- **Active Call Workspace:** Live call control panel with Hold, Resume, Mute, Warm/Cold Agent Transfer, and post-call Dispositions.

### 👥 2. Customer Relationship & Operations
- **Customer Directory & CRM:** Fast telephone lookup, customer history drawer, duplicate detection, and contact management.
- **Queue Management & Routing:** Skills-based routing, real-time waiting queues, SLA tracking, and priority agent distribution.
- **Agent Dashboard & State Controls:** Real-time presence tracking (Available, In Call, Wrap Up, On Break, Offline) with SignalR live updates.

### 📊 3. Analytics, Reporting & Auditability
- **Operations Dashboard:** Live KPI cards (Answer Rate, Average Handle Time, Service Level, Queue Load) and dynamic trend charts.
- **Call History & Timeline Player:** Complete CDR (Call Detail Record) explorer with multi-parameter filtering, duration calculation, and audio playback.
- **Regulatory Audit Trail:** Comprehensive event auditing tracking customer updates, user privileges, disposition logging, and data access.
- **Enterprise RBAC & Security:** Role-Based Access Control (Admin, Supervisor, Agent) enforced across endpoints via JWT Bearer authentication.

---

## 🏗️ Architecture & Technology Stack

### Backend (.NET 8 Clean Architecture)
- **Framework:** ASP.NET Core Web API (.NET 8.0)
- **Pattern:** Clean Architecture / Onion Architecture (Domain, Application, Infrastructure, API)
- **ORM & Database:** Entity Framework Core 8 with SQL Server / LocalDB
- **Real-Time Communication:** Microsoft SignalR Hubs for bidirectional call state broadcasts
- **Testing:** xUnit, FluentAssertions, Moq with 270+ automated integration and unit test coverage

```
backend/
├── src/
│   ├── CallCenter.Api/              # Controllers, SignalR Hubs, Middleware, Dependency Injection
│   ├── CallCenter.Application/      # DTOs, Business Logic, Service Contracts, Validation
│   ├── CallCenter.Domain/           # Core Entities, Aggregates, Enums, Value Objects
│   └── CallCenter.Infrastructure/   # EF Core DbContext, Twilio Telephony, Security, Repositories
└── tests/
    └── CallCenter.Tests/            # Comprehensive integration & unit test suite
```

### Frontend (Modern Angular 19)
- **Framework:** Angular 19 Standalone Architecture
- **State & Reactivity:** RxJS streams, signals, and reactive forms
- **Styling:** Custom bespoke design system featuring glassmorphism, responsive data grids, and accessible typography
- **Telephony Client:** Real-time WebSocket connection to SignalR hubs with Web Audio feedback

```
frontend/
└── src/
    └── app/
        ├── core/            # Auth, Guards, Interceptors, Real-Time SignalR Services
        ├── layout/          # Topbar, Navigation Sidebar, Softphone Dialpad Launcher
        └── pages/           # Dashboard, Active Call, Customers, Queues, History, Reports, Users
```

---

## 🚀 Quick Start Guide

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js](https://nodejs.org/) (v18 or v20 LTS) & npm
- [SQL Server](https://www.microsoft.com/sql-server) or LocalDB (default)

### 1. Clone the Repository
```bash
git clone https://github.com/<your-username>/FalaqFoodCallCenter.git
cd FalaqFoodCallCenter
```

### 2. Run the Backend API
```bash
cd backend
dotnet restore
dotnet run --project src/CallCenter.Api
```
The backend API and Swagger UI will be available at `http://localhost:5289` (or `https://localhost:7194`).

### 3. Run the Frontend Web App
```bash
cd frontend
npm install
npm start
```
Open your browser and navigate to `http://localhost:4200`.

### 4. Running the Automated Test Suite
```bash
# Backend Tests (270+ tests)
cd backend
dotnet test

# Frontend Unit Tests
cd frontend
npm test -- --watch=false
```

---

## 🔐 Default Demo Accounts

| Role | Username | Password |
| :--- | :--- | :--- |
| **Admin** | `admin` | `Admin@123` |
| **Supervisor** | `supervisor` | `Supervisor@123` |
| **Agent** | `agent1` | `Agent@123` |

---

## 📄 License & Attribution
Developed with ❤️ by **Md. Enamul Haque** for Falaq Food Call Center Operations.  
Distributed under the [MIT License](LICENSE).
