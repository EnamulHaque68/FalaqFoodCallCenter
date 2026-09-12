# FalaqFood Call Center - Production Deployment Guide

This document provides exact, end-to-end, copy-paste executable instructions for deploying **FalaqFood Call Center** to an enterprise Linux (Ubuntu 22.04 / 24.04 LTS) production environment.

---

## 1. System Requirements & Prerequisites

### 1.1 Target Host Sizing
- **Operating System**: Ubuntu 22.04 LTS or 24.04 LTS (64-bit x86_64)
- **Compute**: Minimum 4 vCPUs, 8 GB RAM (16 GB RAM recommended for >50 concurrent agents)
- **Storage**: Minimum 100 GB NVMe SSD (dedicated mount for `/var/callcenter/recordings` recommended)
- **Network**: Static public IPv4 address with DNS records pointing to the host

### 1.2 Base Package Installation
Execute as `root` or user with `sudo` privileges:

```bash
# 1. Update operating system packages
sudo apt-get update && sudo apt-get upgrade -y
sudo apt-get install -y curl wget git ufw nginx certbot python3-certbot-nginx jq logrotate unzip

# 2. Add Microsoft Package Repository and install .NET 8 Runtime & SDK
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb
sudo apt-get update
sudo apt-get install -y aspnetcore-runtime-8.0 dotnet-sdk-8.0

# 3. Verify .NET installation
dotnet --info

# 4. Install Node.js 20 LTS & npm
curl -fsSL https://deb.nodesource.com/setup_20.x | sudo -E bash -
sudo apt-get install -y nodejs

# 5. Verify Node.js and npm versions
node -v
npm -v
```

---

## 2. Linux Security, Service User & Filesystem Setup

Create a dedicated unprivileged system user and hardened directory structure:

```bash
# 1. Create dedicated service user without interactive login shell
sudo useradd -r -s /bin/false -d /var/www/falaq-callcenter callcenter-svc || true

# 2. Provision application directory tree
sudo mkdir -p /var/www/falaq-callcenter/api
sudo mkdir -p /var/www/falaq-callcenter/web
sudo mkdir -p /var/callcenter/recordings
sudo mkdir -p /var/log/callcenter
sudo mkdir -p /etc/callcenter
sudo mkdir -p /opt/scripts

# 3. Configure ownership
sudo chown -R callcenter-svc:callcenter-svc /var/www/falaq-callcenter
sudo chown -R callcenter-svc:callcenter-svc /var/callcenter/recordings
sudo chown -R callcenter-svc:callcenter-svc /var/log/callcenter
sudo chown -R root:callcenter-svc /etc/callcenter

# 4. Restrict permissions
sudo chmod 750 /var/www/falaq-callcenter/api
sudo chmod 750 /var/callcenter/recordings
sudo chmod 750 /var/log/callcenter
sudo chmod 750 /etc/callcenter
```

---

## 3. Host Firewall Configuration (UFW)

Protect the host by exposing only essential HTTP, HTTPS, and SSH ports:

```bash
# Reset UFW to secure defaults
sudo ufw default deny incoming
sudo ufw default allow outgoing

# Allow inbound SSH (Adjust port if using non-standard SSH port)
sudo ufw allow 22/tcp comment 'SSH Management'

# Allow inbound Web Traffic (Handled by Nginx reverse proxy)
sudo ufw allow 80/tcp comment 'HTTP (ACME Challenge & Redirection)'
sudo ufw allow 443/tcp comment 'HTTPS (Frontend, API & WebSockets)'

# Explicitly ensure Kestrel (:5000) and SQL Server (:1433) are NOT exposed externally
sudo ufw deny 5000/tcp comment 'Block direct Kestrel access'
sudo ufw deny 1433/tcp comment 'Block direct SQL Server access'

# Enable firewall
sudo ufw --force enable
sudo ufw status verbose
```

---

## 4. SQL Server Provisioning & Migration Execution

### 4.1 SQL Server User & Permission Provisioning
Execute against the production database server using `sqlcmd` or Azure SQL Query Editor:

```sql
-- Connect as DBA / SA
USE [master];
GO

-- Create database if not existing
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'FalaqFoodCallCenterProd')
BEGIN
    CREATE DATABASE [FalaqFoodCallCenterProd]
    COLLATE SQL_Latin1_General_CP1_CI_AS;
END
GO

-- Create dedicated login with strong password
IF NOT EXISTS (SELECT name FROM sys.server_principals WHERE name = N'FalaqCallCenterApp')
BEGIN
    CREATE LOGIN [FalaqCallCenterApp] 
    WITH PASSWORD = '<REPLACE_WITH_STRONG_DB_PASSWORD>', 
    CHECK_POLICY = ON, 
    CHECK_EXPIRATION = OFF;
END
GO

USE [FalaqFoodCallCenterProd];
GO

-- Create application user in database
IF NOT EXISTS (SELECT name FROM sys.database_principals WHERE name = N'FalaqCallCenterApp')
BEGIN
    CREATE USER [FalaqCallCenterApp] FOR LOGIN [FalaqCallCenterApp];
END
GO

-- Grant least-privilege data manipulation roles
ALTER ROLE [db_datareader] ADD MEMBER [FalaqCallCenterApp];
ALTER ROLE [db_datawriter] ADD MEMBER [FalaqCallCenterApp];
ALTER ROLE [db_ddladmin] ADD MEMBER [FalaqCallCenterApp]; -- Required for EF schema updates
GRANT EXECUTE TO [FalaqCallCenterApp];
GO
```

### 4.2 Generate & Execute EF Core Migration Bundle
Execute schema updates using the standalone, zero-dependency migration bundle:

```bash
cd /opt/falaq-source/FalaqFoodCallCenter/backend

# 1. Install or restore local dotnet-ef tool
dotnet tool restore || dotnet tool install --global dotnet-ef

# 2. Build standalone migration bundle
dotnet ef migrations bundle \
  --project src/CallCenter.Infrastructure \
  --startup-project src/CallCenter.Api \
  --output ./efbundle \
  --configuration Release \
  --force

# 3. Apply migrations to production database securely
./efbundle --connection "Server=tcp:sql.callcenter.falaqfood.com,1433;Database=FalaqFoodCallCenterProd;User ID=FalaqCallCenterApp;Password=<REPLACE_WITH_STRONG_DB_PASSWORD>;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"

# 4. Remove local binary after successful execution
rm -f ./efbundle
```

---

## 5. Backend Deployment (.NET 8 Kestrel)

### 5.1 Build & Publish Backend Release
```bash
cd /opt/falaq-source/FalaqFoodCallCenter/backend

# Publish optimized Release build
dotnet publish src/CallCenter.Api/CallCenter.Api.csproj \
  -c Release \
  -o /var/www/falaq-callcenter/api \
  --no-self-contained

# Set file permissions for the service user
sudo chown -R callcenter-svc:callcenter-svc /var/www/falaq-callcenter/api
```

### 5.2 Configure Secure Environment File
Create `/etc/callcenter/api.env`:

```bash
sudo nano /etc/callcenter/api.env
```

Populate the file with production values (refer to [ENVIRONMENT-CONFIG.md](file:///c:/Users/Mamun/Downloads/FalaqFoodCallCenter-CORRECTED/FalaqFoodCallCenter/ENVIRONMENT-CONFIG.md)):

```ini
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:5000
ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
AllowedHosts=api.callcenter.falaqfood.com

# Database Connection String
ConnectionStrings__DefaultConnection=Server=tcp:sql.callcenter.falaqfood.com,1433;Database=FalaqFoodCallCenterProd;User ID=FalaqCallCenterApp;Password=<REPLACE_WITH_STRONG_DB_PASSWORD>;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;MultipleActiveResultSets=True;

# JWT Identity & Security
Jwt__Issuer=https://api.callcenter.falaqfood.com
Jwt__Audience=https://callcenter.falaqfood.com
Jwt__SecretKey=<REPLACE_WITH_SECURE_JWT_SECRET_KEY_AT_LEAST_64_CHARS>
Jwt__AccessTokenMinutes=15

# Telephony Integration
Telephony__ActiveProvider=Real
Telephony__WebhookSecret=<REPLACE_WITH_TELEPHONY_WEBHOOK_SECRET>
Telephony__AccountSid=<REPLACE_WITH_TELEPHONY_ACCOUNT_SID>
Telephony__AuthToken=<REPLACE_WITH_TELEPHONY_AUTH_TOKEN>
Telephony__PublicBaseUrl=https://api.callcenter.falaqfood.com

# Call Recordings Storage & Retention
RecordingStorage__StorageRootPath=/var/callcenter/recordings
RecordingStorage__RetentionDays=90
RecordingStorage__SignedUrlExpirationMinutes=60
RecordingStorage__PurgeWorkerIntervalMinutes=720

# CORS Allowed Origin
Cors__AllowedOrigins__0=https://callcenter.falaqfood.com

# Logging
Logging__LogLevel__Default=Information
Logging__LogLevel__Microsoft.AspNetCore=Warning
```

Lock permissions to `600` so only `root` and `callcenter-svc` can read secrets:
```bash
sudo chown root:callcenter-svc /etc/callcenter/api.env
sudo chmod 600 /etc/callcenter/api.env
```

### 5.3 Configure Hardened Systemd Service
Create `/etc/systemd/system/callcenter-api.service`:

```ini
[Unit]
Description=FalaqFood Call Center ASP.NET Core API Service
After=network.target

[Service]
WorkingDirectory=/var/www/falaq-callcenter/api
ExecStart=/usr/bin/dotnet /var/www/falaq-callcenter/api/CallCenter.Api.dll
Restart=always
RestartSec=5
KillSignal=SIGINT
SyslogIdentifier=callcenter-api
User=callcenter-svc
Group=callcenter-svc

# Environment Variables
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false
EnvironmentFile=/etc/callcenter/api.env

# System Hardening & Sandboxing
PrivateTmp=true
NoNewPrivileges=true
ProtectSystem=full
ProtectHome=true
ReadWritePaths=/var/callcenter/recordings /var/log/callcenter

[Install]
WantedBy=multi-user.target
```

### 5.4 Start and Enable the Service
```bash
sudo systemctl daemon-reload
sudo systemctl enable callcenter-api
sudo systemctl start callcenter-api

# Inspect service state
sudo systemctl status callcenter-api --no-pager
```

---

## 6. Frontend Deployment (Angular SPA)

### 6.1 Build Production Bundle
```bash
cd /opt/falaq-source/FalaqFoodCallCenter/frontend

# Install dependencies deterministically
npm ci

# Build optimized production bundle
npm run build -- --configuration production

# Clean existing deployment directory and copy newly compiled artifacts
sudo rm -rf /var/www/falaq-callcenter/web/*

# Handle Angular 17/18 application builder output path (dist/falaq-food-call-center-web/browser)
if [ -d "dist/falaq-food-call-center-web/browser" ]; then
  sudo cp -r dist/falaq-food-call-center-web/browser/* /var/www/falaq-callcenter/web/
else
  sudo cp -r dist/falaq-food-call-center-web/* /var/www/falaq-callcenter/web/
fi

# Ensure Nginx (www-data) can read web assets
sudo chown -R www-data:www-data /var/www/falaq-callcenter/web
sudo chmod -R 755 /var/www/falaq-callcenter/web
```

---

## 7. Reverse Proxy & HTTPS Configuration (Nginx)

### 7.1 Create Nginx Site Configuration
Create `/etc/nginx/sites-available/callcenter.conf`:

```nginx
# 1. Map for WebSocket Upgrade Header Handling
map $http_upgrade $connection_upgrade {
    default upgrade;
    ''      close;
}

# 2. Backend API, SignalR & Webhooks (api.callcenter.falaqfood.com)
server {
    listen 80;
    listen [::]:80;
    server_name api.callcenter.falaqfood.com;

    # Maximum payload size for audio recording uploads and CRM files
    client_max_body_size 50M;

    # Gzip Compression
    gzip on;
    gzip_vary on;
    gzip_min_length 1024;
    gzip_proxied any;
    gzip_types text/plain text/css application/json application/javascript text/xml application/xml application/xml+rss text/javascript;

    # SignalR Real-Time WebSocket Endpoint
    location /hubs/ {
        proxy_pass http://127.0.0.1:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection $connection_upgrade;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;

        # Disable buffering and cache for zero-latency real-time events
        proxy_buffering off;
        proxy_cache off;
        proxy_read_timeout 3600s;
        proxy_send_timeout 3600s;
    }

    # REST APIs, Webhooks & Health Probes
    location / {
        proxy_pass http://127.0.0.1:5000;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 60s;
        proxy_connect_timeout 15s;
    }

    # Security Headers
    add_header X-Frame-Options "DENY" always;
    add_header X-Content-Type-Options "nosniff" always;
    add_header Referrer-Policy "strict-origin-when-cross-origin" always;
    add_header Strict-Transport-Security "max-age=31536000; includeSubDomains; preload" always;
}

# 3. Frontend Web Application (callcenter.falaqfood.com)
server {
    listen 80;
    listen [::]:80;
    server_name callcenter.falaqfood.com;

    root /var/www/falaq-callcenter/web;
    index index.html;

    # Gzip Compression
    gzip on;
    gzip_vary on;
    gzip_min_length 1024;
    gzip_proxied any;
    gzip_types text/plain text/css application/json application/javascript text/xml application/xml text/javascript image/svg+xml;

    # Immutable Cache for Content-Hashed Static Assets (1 Year)
    location ~* \.(?:css|js|woff2?|eot|ttf|otf|svg|png|jpg|jpeg|gif|ico|webp)$ {
        expires 1y;
        add_header Cache-Control "public, immutable";
        access_log off;
    }

    # Angular SPA HTML5 Routing Fallback (Never cache index.html)
    location / {
        try_files $uri $uri/ /index.html;
        add_header Cache-Control "no-cache, no-store, must-revalidate";
    }

    # Security Headers
    add_header X-Frame-Options "DENY" always;
    add_header X-Content-Type-Options "nosniff" always;
    add_header Referrer-Policy "strict-origin-when-cross-origin" always;
    add_header Strict-Transport-Security "max-age=31536000; includeSubDomains; preload" always;
}
```

### 7.2 Enable Site & Verify Nginx Syntax
```bash
# Link configuration to sites-enabled
sudo ln -sf /etc/nginx/sites-available/callcenter.conf /etc/nginx/sites-enabled/

# Remove default site if present
sudo rm -f /etc/nginx/sites-enabled/default

# Test Nginx configuration
sudo nginx -t

# Reload Nginx
sudo systemctl reload nginx
```

### 7.3 Provision Automated SSL/TLS Certificates via Certbot
```bash
# Request Let's Encrypt certificates and auto-configure HTTPS redirects
sudo certbot --nginx \
  -d callcenter.falaqfood.com \
  -d api.callcenter.falaqfood.com \
  --non-interactive \
  --agree-tos \
  -m devops@falaqfood.com \
  --redirect

# Verify automated renewal dry-run
sudo certbot renew --dry-run
```

---

## 8. Health Check Probing & Verification Runbook

Execute these validation commands from the server terminal:

```bash
# 1. Liveness Probe (Tests ASP.NET Core Kestrel process responsiveness)
curl -s -o /dev/null -w "%{http_code}\n" https://api.callcenter.falaqfood.com/health/live
# Expected Output: 200

# 2. Readiness Probe (Tests SQL Server database connectivity)
curl -s https://api.callcenter.falaqfood.com/health/ready
# Expected Output: Healthy (or JSON payload with status: "Healthy")

# 3. Frontend Web Delivery & HTTP/2 Verification
curl -I https://callcenter.falaqfood.com
# Expected: HTTP/2 200 OK with strict security headers

# 4. SignalR Real-Time Negotiate Endpoint Test
curl -X POST https://api.callcenter.falaqfood.com/hubs/call-center/negotiate?negotiateVersion=1
# Expected: JSON response containing "connectionId" and "availableTransports"

# 5. Review Backend Production Logs
sudo journalctl -u callcenter-api -n 50 --no-pager
```

---

## 9. Automated Backup Configuration

Create the automated database backup cron job:

```bash
# 1. Save backup script to /opt/scripts/backup-database.sh
sudo chmod 700 /opt/scripts/backup-database.sh
sudo chown root:root /opt/scripts/backup-database.sh

# 2. Create automated cron schedule (/etc/cron.d/callcenter-backup)
echo "0 1 * * * root /opt/scripts/backup-database.sh >> /var/log/callcenter/backup.log 2>&1" | sudo tee /etc/cron.d/callcenter-backup

# 3. Verify permissions
sudo chmod 644 /etc/cron.d/callcenter-backup
```

---

## 10. Zero-Downtime Application Update Procedure

When deploying subsequent application releases:

```bash
#!/usr/bin/env bash
set -euo pipefail

RELEASE_DIR="/opt/releases/$(date +%Y%m%d_%H%M%S)"
mkdir -p "${RELEASE_DIR}"

echo "1. Building backend Release..."
cd /opt/falaq-source/FalaqFoodCallCenter/backend
git pull origin main
dotnet publish src/CallCenter.Api/CallCenter.Api.csproj -c Release -o "${RELEASE_DIR}/api" --no-self-contained

echo "2. Applying database migrations..."
dotnet ef database update --project src/CallCenter.Infrastructure --startup-project src/CallCenter.Api --configuration Release

echo "3. Building frontend Release..."
cd /opt/falaq-source/FalaqFoodCallCenter/frontend
npm ci
npm run build -- --configuration production

echo "4. Swapping binaries with atomic backup..."
sudo cp -r /var/www/falaq-callcenter/api /var/www/falaq-callcenter/api-backup
sudo cp -r "${RELEASE_DIR}/api/"* /var/www/falaq-callcenter/api/
sudo chown -R callcenter-svc:callcenter-svc /var/www/falaq-callcenter/api

echo "5. Restarting backend service..."
sudo systemctl restart callcenter-api

echo "6. Deploying static web assets..."
sudo cp -r /var/www/falaq-callcenter/web /var/www/falaq-callcenter/web-backup
if [ -d "dist/falaq-food-call-center-web/browser" ]; then
  sudo cp -r dist/falaq-food-call-center-web/browser/* /var/www/falaq-callcenter/web/
else
  sudo cp -r dist/falaq-food-call-center-web/* /var/www/falaq-callcenter/web/
fi
sudo chown -R www-data:www-data /var/www/falaq-callcenter/web

echo "7. Verifying production readiness..."
sleep 3
curl -f https://api.callcenter.falaqfood.com/health/ready
echo "Deployment completed successfully!"
```

---

## 11. Rollback Runbook

If a critical fault is detected immediately following a deployment:

```bash
# 1. Stop backend service
sudo systemctl stop callcenter-api

# 2. Restore previous binaries from backup snapshot
sudo cp -r /var/www/falaq-callcenter/api-backup/* /var/www/falaq-callcenter/api/
sudo cp -r /var/www/falaq-callcenter/web-backup/* /var/www/falaq-callcenter/web/

# 3. Ensure permissions
sudo chown -R callcenter-svc:callcenter-svc /var/www/falaq-callcenter/api
sudo chown -R www-data:www-data /var/www/falaq-callcenter/web

# 4. Restart backend and reload proxy
sudo systemctl start callcenter-api
sudo systemctl reload nginx

# 5. Confirm system recovery
curl -f https://api.callcenter.falaqfood.com/health/ready
```
