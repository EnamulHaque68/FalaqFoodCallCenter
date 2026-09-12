# FalaqFood Call Center - Master Environment Configuration & Secrets Reference

This document provides the definitive reference for all configuration variables, environment overrides, secrets management policies, and key generation protocols for **FalaqFood Call Center**.

---

## 1. Secrets Security & Governance Policy

> [!IMPORTANT]
> **Zero Plaintext Secrets Rule**:
> - Never commit plaintext passwords, private cryptographic keys, or carrier tokens into source control.
> - Never store secrets in client-facing frontend source files (`environment.ts`, `environment.prod.ts`).
> - Production secrets must be injected at runtime via protected mechanisms:
>   - **Linux Bare-Metal / VM**: Encrypted or permission-locked systemd `EnvironmentFile` (`/etc/callcenter/api.env` with permissions `600`).
>   - **Containerized / Kubernetes**: Docker Secrets or Kubernetes `Secret` resources mapped to environment variables.
>   - **Cloud Hosted**: Managed key vaults (Azure Key Vault, AWS Secrets Manager, or HashiCorp Vault) using Managed Identity.

### 1.1 Secret Rotation Schedules & Impact Analysis
| Secret / Key | Recommended Rotation Frequency | Operational Impact of Rotation | Migration Procedure |
| :--- | :--- | :--- | :--- |
| **JWT Secret Key** | Every 90 Days | Invalidates existing agent/admin JWTs (requires re-login) | Deploy key rotation off-shift or support dual-key verification window |
| **SQL Server Password** | Every 180 Days | Temporary connection disruption during service reload | Update SQL login password, update `/etc/callcenter/api.env`, restart backend |
| **Telephony Webhook Secret** | Every 180 Days | Carrier webhooks fail if keys are unsynchronized | Configure secondary key in carrier dashboard before activating primary |
| **Carrier Auth Token** | Annually or upon Compromise | Outbound calling or SMS delivery disabled | Re-issue in carrier console, update env file, reload service |

---

## 2. Backend Configuration Reference (.NET 8)

ASP.NET Core automatically maps hierarchical environment variables using double underscores (`__`) as section delimiters (e.g., `Jwt__SecretKey`).

### 2.1 Database Configuration (`ConnectionStrings`)
| Variable Name | Data Type | Required | Description / Production Recommendation |
| :--- | :---: | :---: | :--- |
| `ConnectionStrings__DefaultConnection` | String | **Yes** | Fully qualified ADO.NET / EF Core connection string targeting SQL Server.<br>`Server=tcp:<SQL_HOST>,1433;Database=FalaqFoodCallCenterProd;User ID=<USER>;Password=<PASSWORD>;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;MultipleActiveResultSets=True;` |
| `ConnectionStrings__Redis` | String | Optional | Redis connection string for multi-node SignalR clustering.<br>`redis.internal.falaqfood.com:6379,password=<REDIS_PASSWORD>,ssl=True,abortConnect=False` |

### 2.2 Authentication & JWT Identity (`Jwt`)
| Variable Name | Data Type | Required | Description |
| :--- | :---: | :---: | :--- |
| `Jwt__Issuer` | String | **Yes** | Identity issuer URI. Recommended: `https://api.callcenter.falaqfood.com` |
| `Jwt__Audience` | String | **Yes** | Client audience URI. Recommended: `https://callcenter.falaqfood.com` |
| `Jwt__SecretKey` | String | **Yes** | High-entropy HMAC-SHA256 signing key. **Must be at least 64 characters (512 bits)**. Generated via `openssl rand -base64 48`. |
| `Jwt__AccessTokenMinutes` | Integer | No (Default: 15) | Access token lifespan in minutes. Recommended: `15` to `30` for call center security. |

### 2.3 Telephony Carrier Subsystem (`Telephony`)
| Variable Name | Data Type | Required | Description |
| :--- | :---: | :---: | :--- |
| `Telephony__ActiveProvider` | String | **Yes** | Active telephony driver. Production options: `Real` or `Twilio`. (Use `Simulated` only for staging/dev). |
| `Telephony__WebhookSecret` | String | **Yes** | Shared secret string for validating inbound HMAC-SHA256 signatures on webhook payloads. |
| `Telephony__AccountSid` | String | Conditional | Carrier Account SID (required when `Twilio` provider is active). |
| `Telephony__AuthToken` | String | Conditional | Carrier API Auth Token (required when `Twilio` provider is active). |
| `Telephony__PublicBaseUrl` | String | **Yes** | Public-facing domain for carrier callbacks and audio webhooks: `https://api.callcenter.falaqfood.com`. |

### 2.4 Call Recording Storage & Retention (`RecordingStorage`)
| Variable Name | Data Type | Required | Description |
| :--- | :---: | :---: | :--- |
| `RecordingStorage__StorageRootPath` | String | **Yes** | Absolute directory path where audio files are written: `/var/callcenter/recordings`. |
| `RecordingStorage__RetentionDays` | Integer | No (Default: 90) | Audio retention lifecycle window in days. Recordings older than this are purged. |
| `RecordingStorage__SignedUrlExpirationMinutes` | Integer | No (Default: 60) | Lifespan in minutes for HMAC-signed audio playback links. |
| `RecordingStorage__PurgeWorkerIntervalMinutes` | Integer | No (Default: 720) | Execution frequency for the automated disk cleanup background worker (720 min = twice daily). |

### 2.5 Reverse Proxy & Host Binding (`Kestrel`)
| Variable Name | Data Type | Required | Description |
| :--- | :---: | :---: | :--- |
| `ASPNETCORE_ENVIRONMENT` | String | **Yes** | Must be set to `Production`. |
| `ASPNETCORE_URLS` | String | **Yes** | Local Kestrel binding: `http://127.0.0.1:5000`. |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | Boolean | **Yes** | Must be `true` to ensure client IP and HTTPS protocol are correctly extracted from Nginx headers. |
| `AllowedHosts` | String | **Yes** | Host header filter: `api.callcenter.falaqfood.com`. |
| `Cors__AllowedOrigins__0` | String | **Yes** | CORS origin allowed to send credentials and call APIs: `https://callcenter.falaqfood.com`. |

### 2.6 Logging & Diagnostics (`Logging`)
| Variable Name | Data Type | Required | Description |
| :--- | :---: | :---: | :--- |
| `Logging__LogLevel__Default` | String | No (Default: Information) | General application log verbosity (`Information`, `Warning`, `Error`). |
| `Logging__LogLevel__Microsoft.AspNetCore` | String | No (Default: Warning) | Web server framework logging level (keep at `Warning` to avoid log spam). |
| `Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command` | String | No (Default: Warning) | Database SQL query logging (keep at `Warning` to prevent PII leakage). |

---

## 3. Frontend Configuration Reference (Angular SPA)

The Angular frontend relies on build-time environment files compiled into JavaScript bundles.

### 3.1 `src/environments/environment.prod.ts`
```typescript
export const environment = {
  production: true,
  apiBaseUrl: 'https://api.callcenter.falaqfood.com/api/v1',
  signalRHubUrl: 'https://api.callcenter.falaqfood.com/hubs/call-center'
} as const;
```

### 3.2 Property Descriptions
| Property | Type | Description |
| :--- | :--- | :--- |
| `production` | Boolean | Activates Angular production mode (disables runtime assertions and debugging checks). |
| `apiBaseUrl` | String | Fully qualified HTTPS endpoint pointing to the REST API route prefix. |
| `signalRHubUrl` | String | Fully qualified WSS/HTTPS endpoint pointing to the SignalR real-time hub. |

> [!CAUTION]
> **No Frontend Secrets**: Never place API keys, carrier credentials, database connection strings, or JWT signing keys into Angular environment files. Anything compiled into frontend code is publicly extractable by any client browser.

---

## 4. Cryptographic Secret Generation Utilities

Execute these commands in a secure Linux terminal to produce cryptographically strong, high-entropy secrets for production deployment:

```bash
# 1. Generate 64-character Base64 string for Jwt:SecretKey
openssl rand -base64 48

# 2. Generate 48-character Hex string for Telephony:WebhookSecret
openssl rand -hex 24

# 3. Generate 32-character complex Database Password
openssl rand -base64 24

# 4. Generate 64-character Hex string for Redis Password (if using Redis backplane)
openssl rand -hex 32
```

---

## 5. Master Production Environment Template (`/etc/callcenter/api.env`)

Below is the complete, annotated production environment configuration template. Copy to `/etc/callcenter/api.env` and replace all `<PLACEHOLDER>` tags with real secrets:

```ini
# ==============================================================================
# FalaqFood Call Center - Production Environment Configuration
# Location: /etc/callcenter/api.env
# Permissions: chmod 600 /etc/callcenter/api.env
# ==============================================================================

# Core ASP.NET Core Runtime Settings
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:5000
ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
AllowedHosts=api.callcenter.falaqfood.com

# Database Connection String (Enforces TLS encryption and trusted CA verification)
ConnectionStrings__DefaultConnection=Server=tcp:sql.callcenter.falaqfood.com,1433;Database=FalaqFoodCallCenterProd;User ID=FalaqCallCenterApp;Password=<PLACEHOLDER_STRONG_DB_PASSWORD>;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;MultipleActiveResultSets=True;

# JWT Authentication & Token Signing
Jwt__Issuer=https://api.callcenter.falaqfood.com
Jwt__Audience=https://callcenter.falaqfood.com
Jwt__SecretKey=<PLACEHOLDER_JWT_SECRET_KEY_MINIMUM_64_CHARACTERS>
Jwt__AccessTokenMinutes=15

# Telephony Carrier Integration
Telephony__ActiveProvider=Real
Telephony__WebhookSecret=<PLACEHOLDER_TELEPHONY_WEBHOOK_SECRET>
Telephony__AccountSid=<PLACEHOLDER_CARRIER_ACCOUNT_SID>
Telephony__AuthToken=<PLACEHOLDER_CARRIER_AUTH_TOKEN>
Telephony__PublicBaseUrl=https://api.callcenter.falaqfood.com

# Audio Recordings Management & Retention
RecordingStorage__StorageRootPath=/var/callcenter/recordings
RecordingStorage__RetentionDays=90
RecordingStorage__SignedUrlExpirationMinutes=60
RecordingStorage__PurgeWorkerIntervalMinutes=720

# Cross-Origin Resource Sharing (CORS)
Cors__AllowedOrigins__0=https://callcenter.falaqfood.com

# Structured Logging Verbosity
Logging__LogLevel__Default=Information
Logging__LogLevel__Microsoft.AspNetCore=Warning
Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command=Warning

# Optional: Redis SignalR Backplane (Uncomment if running multi-node cluster)
# ConnectionStrings__Redis=redis.internal.falaqfood.com:6379,password=<PLACEHOLDER_REDIS_PASSWORD>,ssl=True,abortConnect=False
```

---

## 6. Environment Troubleshooting & Validation Matrix

| Symptom / Error | Root Cause | Remediation Step |
| :--- | :--- | :--- |
| **CORS policy block in browser console** | `Cors__AllowedOrigins__0` does not match the frontend origin exactly (trailing slash, protocol mismatch). | Ensure `Cors__AllowedOrigins__0=https://callcenter.falaqfood.com` matches the browser address bar with no trailing slash. |
| **SignalR negotiate returns 404** | Nginx does not proxy `/hubs/` with WebSocket upgrade headers or routes are misconfigured. | Verify `/etc/nginx/sites-available/callcenter.conf` includes the `location /hubs/` block with `proxy_http_version 1.1;` and `$connection_upgrade`. |
| **JWT verification fails (`401 Unauthorized`)** | `Jwt__SecretKey` length is less than 32 characters or `Jwt__Issuer`/`Jwt__Audience` differs between token creation and validation. | Ensure `Jwt__SecretKey` is at least 64 characters and identical across service restarts. |
| **Telephony webhooks rejected (`401/403`)** | `Telephony__WebhookSecret` in `api.env` does not match the secret registered in the carrier portal. | Verify carrier signing secret matches `Telephony__WebhookSecret` exactly. |
| **Database connection failure on startup** | SQL Server firewall is blocking port 1433 or `TrustServerCertificate=False` with an untrusted internal CA. | Install the CA certificate into the host trust store (`/usr/local/share/ca-certificates/`) or verify SQL Server network routing. |
