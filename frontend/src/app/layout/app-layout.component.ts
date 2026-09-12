import { ChangeDetectionStrategy, ChangeDetectorRef, Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Subscription } from 'rxjs';
import { AuthService } from '../core/auth/auth.service';
import { CallCenterRealtimeService, NotificationEvent } from '../core/services/call-center-realtime.service';
import { SoftphoneDialerComponent } from './softphone-dialer/softphone-dialer.component';

@Component({
  selector: 'app-layout',
  standalone: true,
  imports: [CommonModule, RouterOutlet, RouterLink, RouterLinkActive, SoftphoneDialerComponent],
  template: `
    <div class="app-shell">
      <aside class="sidebar">
        <div class="brand">
          <span class="brand-mark">FF</span>
          <div>
            <strong>Falaq Food</strong>
            <small>Call Center</small>
          </div>
        </div>

        <nav>
          @if (isSupervisorOrAdmin()) {
            <a routerLink="/dashboard" routerLinkActive="active">Operations Dashboard</a>
          }

          <a routerLink="/agent-dashboard" class="nav-section-link" title="Open Agent Workspace">
            <span>Workspace</span>
            <span class="section-arrow">↗</span>
          </a>
          <a routerLink="/agent-dashboard" routerLinkActive="active">Agent Workspace</a>
          <a routerLink="/active-call" routerLinkActive="active">Active Call Workspace</a>
          <a routerLink="/incoming-call" routerLinkActive="active">Incoming Call Center</a>
          <a routerLink="/customers" routerLinkActive="active">Customers</a>
          <a routerLink="/call-history" routerLinkActive="active">Call History</a>

          @if (isSupervisorOrAdmin()) {
            <a routerLink="/queues" class="nav-section-link" title="Open Management Queues">
              <span>Management</span>
              <span class="section-arrow">↗</span>
            </a>
            <a routerLink="/queues" routerLinkActive="active">Call Queues</a>
            <a routerLink="/reports" routerLinkActive="active">Reports</a>
            <a routerLink="/agents" routerLinkActive="active">Agent Management</a>
          } @else {
            <div class="nav-section">Management</div>
            <span class="nav-disabled" title="Requires Supervisor or Admin role">Call Queues (Restricted)</span>
            <span class="nav-disabled" title="Requires Supervisor or Admin role">Reports (Restricted)</span>
            <span class="nav-disabled" title="Requires Supervisor or Admin role">Agent Management (Restricted)</span>
          }

          @if (auth.hasRole('Admin')) {
            <a routerLink="/settings" class="nav-section-link" title="Open System Settings">
              <span>System</span>
              <span class="section-arrow">↗</span>
            </a>
            <a routerLink="/users" routerLinkActive="active">User Management</a>
            <a routerLink="/audit-logs" routerLinkActive="active">Audit Logs</a>
            <a routerLink="/settings" routerLinkActive="active">Settings</a>
          } @else {
            <div class="nav-section">System</div>
            <span class="nav-disabled" title="Requires the Administrator role">User Management (Restricted)</span>
            <span class="nav-disabled" title="Requires the Administrator role">Audit Logs (Restricted)</span>
            <span class="nav-disabled" title="Requires the Administrator role">Settings (Restricted)</span>
          }
        </nav>

        <div class="sidebar-security-badge">
          <strong style="display:block;color:#e2e8f0;margin-bottom:4px;">🛡️ Security Boundary</strong>
          Protected by ASP.NET Core JWT & RBAC policy authorization.
        </div>
      </aside>

      <main class="main-content">
        <header class="topbar">
          <div>
            <span class="eyebrow">Operations</span>
            <h1>Call Center Platform</h1>
          </div>
          <div class="topbar-actions">
            <!-- Dedicated Softphone Dialpad Button -->
            <button
              type="button"
              class="topbar-softphone-btn"
              [class.active]="showDialer"
              (click)="toggleDialer()"
              title="Open Browser Softphone & Dialpad">
              <span class="phone-icon-pulse">📞</span>
              <strong>Softphone</strong>
              <span class="softphone-pulse-dot"></span>
            </button>

            <span class="status-dot"></span>
            <span>{{ auth.currentUser()?.username || 'Operator' }}</span>
            <span
              class="badge-role"
              [class]="'badge-' + (auth.userRole().toLowerCase() || 'guest')">
              {{ auth.userRole() || 'Agent' }}
            </span>
            <button type="button" class="btn-signout" (click)="logout()">Sign out</button>
          </div>
        </header>

        <!-- Realtime Notification Toast -->
        @if (activeNotification) {
          <div class="realtime-toast" [ngClass]="getToastClass(activeNotification.severity)">
            <div class="toast-content">
              <strong>🔔 {{ activeNotification.title }}</strong>
              <p>{{ activeNotification.message }}</p>
            </div>
            <button type="button" class="toast-close" (click)="dismissNotification()">✕</button>
          </div>
        }

        <section class="content">
          <router-outlet />
        </section>

        <!-- Global Softphone Dialpad Component -->
        @if (showDialer) {
          <app-softphone-dialer
            [isOpen]="showDialer"
            (close)="showDialer = false" />
        }

        <!-- Floating Softphone Launcher Pill (when dialer is closed) -->
        @if (!showDialer) {
          <button
            type="button"
            class="floating-softphone-pill"
            (click)="showDialer = true"
            title="Open Telephony Softphone">
            <span class="pill-phone-icon">📞</span>
            <span class="pill-text">Softphone Dialpad</span>
            <span class="pill-dot"></span>
          </button>
        }
      </main>
    </div>
  `,
  styles: [`
    .realtime-toast {
      margin: 12px 24px 0;
      padding: 12px 16px;
      border-radius: 8px;
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 12px;
      font-size: 13px;
      animation: slideIn 0.2s ease-out;
      box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1);
    }
    @keyframes slideIn {
      from { transform: translateY(-10px); opacity: 0; }
      to { transform: translateY(0); opacity: 1; }
    }
    .toast-info { background: #eff6ff; border: 1px solid #bfdbfe; color: #1e40af; }
    .toast-warning { background: #fffbeb; border: 1px solid #fde68a; color: #92400e; }
    .toast-error { background: #fef2f2; border: 1px solid #fecaca; color: #991b1b; }
    .toast-success { background: #f0fdf4; border: 1px solid #bbf7d0; color: #166534; }
    .toast-content p { margin: 2px 0 0; font-size: 12px; opacity: 0.9; }
    .toast-close {
      background: none;
      border: none;
      font-size: 16px;
      cursor: pointer;
      color: inherit;
      padding: 0 4px;
    }
    .nav-section-link {
      display: flex !important;
      align-items: center;
      justify-content: space-between;
      font-size: 11px !important;
      text-transform: uppercase;
      letter-spacing: .12em;
      color: #94a3b8 !important;
      margin: 18px 0 6px !important;
      padding: 7px 10px !important;
      border-radius: 6px;
      font-weight: 750;
      text-decoration: none;
      transition: all 0.15s ease-in-out;
      cursor: pointer;
    }
    .nav-section-link:hover {
      background: rgba(255, 255, 255, 0.08);
      color: #ffffff !important;
    }
    .section-arrow {
      font-size: 12px;
      opacity: 0.6;
      transition: transform 0.15s ease-in-out, opacity 0.15s;
    }
    .nav-section-link:hover .section-arrow {
      transform: translateY(-1px) translateX(1px);
      opacity: 1;
    }
    .topbar-softphone-btn {
      display: inline-flex;
      align-items: center;
      gap: 7px;
      background: linear-gradient(135deg, #10b981, #059669) !important;
      color: #ffffff !important;
      border: 1px solid rgba(255, 255, 255, 0.2) !important;
      padding: 7px 14px !important;
      border-radius: 8px !important;
      font-weight: 700;
      font-size: 12px;
      cursor: pointer;
      box-shadow: 0 2px 8px rgba(16, 185, 129, 0.35);
      transition: all 0.15s ease-in-out;
    }
    .topbar-softphone-btn:hover {
      background: linear-gradient(135deg, #059669, #047857) !important;
      box-shadow: 0 4px 12px rgba(16, 185, 129, 0.5);
      transform: translateY(-1px);
    }
    .topbar-softphone-btn.active {
      background: #047857 !important;
      box-shadow: inset 0 2px 4px rgba(0, 0, 0, 0.2);
    }
    .phone-icon-pulse {
      font-size: 14px;
      animation: phoneWiggle 2.5s infinite ease-in-out;
    }
    @keyframes phoneWiggle {
      0%, 80%, 100% { transform: rotate(0deg); }
      85% { transform: rotate(-15deg); }
      90% { transform: rotate(15deg); }
      95% { transform: rotate(-10deg); }
    }
    .softphone-pulse-dot {
      width: 7px;
      height: 7px;
      background: #86efac;
      border-radius: 50%;
      box-shadow: 0 0 6px #86efac;
      animation: pulseGreen 1.5s infinite;
    }
    @keyframes pulseGreen {
      0% { transform: scale(0.9); opacity: 0.8; }
      50% { transform: scale(1.3); opacity: 1; }
      100% { transform: scale(0.9); opacity: 0.8; }
    }
    .floating-softphone-pill {
      position: fixed;
      bottom: 24px;
      right: 24px;
      display: inline-flex;
      align-items: center;
      gap: 9px;
      background: #0d1628;
      border: 1px solid rgba(255, 255, 255, 0.2);
      color: #fff;
      padding: 10px 18px;
      border-radius: 9999px;
      font-size: 13px;
      font-weight: 700;
      cursor: pointer;
      box-shadow: 0 10px 25px -5px rgba(0, 0, 0, 0.4), 0 0 0 1px rgba(255, 255, 255, 0.1);
      z-index: 999;
      transition: all 0.2s cubic-bezier(0.16, 1, 0.3, 1);
    }
    .floating-softphone-pill:hover {
      background: #17233a;
      transform: translateY(-2px);
      box-shadow: 0 15px 30px -5px rgba(0, 0, 0, 0.5), 0 0 0 2px rgba(16, 185, 129, 0.4);
    }
    .pill-phone-icon {
      font-size: 16px;
    }
    .pill-dot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
      background: #10b981;
      box-shadow: 0 0 8px #10b981;
    }
    .btn-signout {
      border: 1px solid #dbe1ea;
      background: #fff;
      padding: 7px 12px;
      border-radius: 7px;
      cursor: pointer;
      font-size: 12px;
      font-weight: 600;
      color: #64748b;
      transition: all 0.12s;
    }
    .btn-signout:hover {
      background: #fee2e2;
      color: #ef4444;
      border-color: #fecaca;
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AppLayoutComponent implements OnInit, OnDestroy {
  readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly realtime = inject(CallCenterRealtimeService);
  private readonly cdr = inject(ChangeDetectorRef);

  showDialer = false;
  activeNotification: NotificationEvent | null = null;
  private notificationSub?: Subscription;

  toggleDialer(): void {
    this.showDialer = !this.showDialer;
    this.cdr.markForCheck();
  }

  ngOnInit(): void {
    if (this.auth.isAuthenticated()) {
      this.realtime.connect();
      this.notificationSub = this.realtime.notifications$.subscribe(notif => {
        this.activeNotification = notif;
        this.cdr.markForCheck();
        setTimeout(() => {
          if (this.activeNotification?.id === notif.id) {
            this.activeNotification = null;
            this.cdr.markForCheck();
          }
        }, 6000);
      });
    }
  }

  ngOnDestroy(): void {
    this.notificationSub?.unsubscribe();
  }

  isSupervisorOrAdmin(): boolean {
    return this.auth.hasRole(['Admin', 'Supervisor']);
  }

  dismissNotification(): void {
    this.activeNotification = null;
    this.cdr.markForCheck();
  }

  getToastClass(severity?: string): string {
    const s = (severity || '').toLowerCase();
    if (s === 'warning') return 'toast-warning';
    if (s === 'error') return 'toast-error';
    if (s === 'success') return 'toast-success';
    return 'toast-info';
  }

  logout(): void {
    void this.realtime.stop();
    this.auth.logout();
    void this.router.navigate(['/login']);
  }
}
