import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  inject,
  OnDestroy,
  OnInit
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { debounceTime, finalize, Subscription } from 'rxjs';
import { ReportsService } from '../../core/services/reports.service';
import { AuthService } from '../../core/auth/auth.service';
import { CallCenterRealtimeService } from '../../core/services/call-center-realtime.service';
import { OperationsDashboard } from '../../core/models/call-history.models';
import { MetricKpiCardComponent } from './components/metric-kpi-card.component';
import { PeriodStatsCardComponent } from './components/period-stats-card.component';
import { CallTrendChartComponent } from './components/call-trend-chart.component';
import { AgentStatusGridComponent } from './components/agent-status-grid.component';
import { QueueStatusWidgetComponent } from './components/queue-status-widget.component';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [
    CommonModule,
    MetricKpiCardComponent,
    PeriodStatsCardComponent,
    CallTrendChartComponent,
    AgentStatusGridComponent,
    QueueStatusWidgetComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="dashboard-shell">
      <!-- Top Command Bar -->
      <header class="command-bar">
        <div class="command-info">
          <div class="pulse-tag">
            <span class="pulse-indicator"></span>
            <span>Live Executive Operations</span>
          </div>
          <h2 class="command-title">Command Center Dashboard</h2>
          <p class="command-subtitle">
            Enterprise telephonic metrics, agent presence, and real-time inbound traffic.
          </p>
        </div>

        <div class="command-actions">
          <div class="auto-refresh-toggle">
            <label class="toggle-label">
              <input
                type="checkbox"
                [checked]="autoRefreshEnabled"
                (change)="toggleAutoRefresh()" />
              <span>Auto-refresh (30s)</span>
            </label>
          </div>

          <div class="refresh-meta">
            <span class="last-sync">Updated {{ lastRefreshedAt | date:'HH:mm:ss' }}</span>
            <button
              type="button"
              class="refresh-btn"
              [disabled]="loading"
              (click)="load(false)">
              <span class="refresh-icon" [class.spinning]="loading">↻</span>
              <span>Refresh</span>
            </button>
          </div>
        </div>
      </header>

      <!-- Error Notification Banner -->
      @if (error) {
        <div class="error-banner">
          <div class="error-content">
            <span class="error-icon">⚠️</span>
            <span>Unable to retrieve live operations telemetry from the server.</span>
          </div>
          <button type="button" class="retry-btn" (click)="load(true)">Retry</button>
        </div>
      }

      <!-- Full-Page Loading Skeleton -->
      @if (loading && !dashboard) {
        <div class="loading-state">
          <div class="loading-spinner"></div>
          <p>Syncing live operational telemetry & agent statuses…</p>
        </div>
      }

      <!-- Main Operational Dashboard Content -->
      @if (dashboard) {
        <!-- 10 CORE METRICS RIBBON -->
        <section class="kpi-ribbon">
          <app-metric-kpi-card
            label="Total Calls"
            [value]="dashboard.metrics.totalCalls"
            icon="📞"
            subtext="All initiated calls"
            theme="default" />

          <app-metric-kpi-card
            label="Incoming"
            [value]="dashboard.metrics.incoming"
            icon="📥"
            subtext="Inbound callers"
            theme="primary" />

          <app-metric-kpi-card
            label="Outgoing"
            [value]="dashboard.metrics.outgoing"
            icon="📤"
            subtext="Outbound dials"
            theme="cyan" />

          <app-metric-kpi-card
            label="Completed"
            [value]="dashboard.metrics.completed"
            icon="✅"
            subtext="Resolved calls"
            theme="success" />

          <app-metric-kpi-card
            label="Missed"
            [value]="dashboard.metrics.missed"
            icon="❌"
            subtext="Abandoned / Dropped"
            theme="danger" />

          <app-metric-kpi-card
            label="Rejected"
            [value]="dashboard.metrics.rejected"
            icon="🚫"
            subtext="Caller rejected"
            theme="warning" />

          <app-metric-kpi-card
            label="Avg Duration"
            [value]="formatDuration(dashboard.metrics.averageDurationSeconds)"
            icon="⏱️"
            subtext="Minutes : Seconds"
            theme="purple" />

          <app-metric-kpi-card
            label="Available Agents"
            [value]="dashboard.metrics.availableAgents"
            icon="🟢"
            subtext="Ready for calls"
            theme="success" />

          <app-metric-kpi-card
            label="Busy Agents"
            [value]="dashboard.metrics.busyAgents"
            icon="🟡"
            subtext="On active call"
            theme="warning" />

          <app-metric-kpi-card
            label="Queue Size"
            [value]="dashboard.metrics.queueSize"
            icon="👥"
            subtext="Waiting in queue"
            [theme]="dashboard.metrics.queueSize > 0 ? 'danger' : 'default'" />
        </section>

        <!-- HISTORICAL PERIOD STATISTICS (Daily, Weekly, Monthly) -->
        <section class="period-stats-grid">
          <app-period-stats-card
            [stats]="dashboard.dailyStats"
            title="Today's Performance"
            subtitle="Current Shift"
            theme="blue" />

          <app-period-stats-card
            [stats]="dashboard.weeklyStats"
            title="Weekly Velocity"
            subtitle="Trailing 7 Days"
            theme="purple" />

          <app-period-stats-card
            [stats]="dashboard.monthlyStats"
            title="Monthly Aggregate"
            subtitle="Trailing 30 Days"
            theme="teal" />
        </section>

        <!-- CALL VOLUME TREND CHART -->
        <section class="trend-section">
          <app-call-trend-chart [trends]="dashboard.callTrends" />
        </section>

        <!-- WORKFORCE & ROUTING SPLIT SECTION -->
        <section class="operations-split">
          <div class="split-roster">
            <app-agent-status-grid [summary]="dashboard.agentStatus" />
          </div>

          <div class="split-queue">
            <app-queue-status-widget [queueStatus]="dashboard.queueStatus" />
          </div>
        </section>

        <!-- BUSINESS OUTCOMES & DISPOSITIONS SECTION -->
        @if (dashboard.dispositionBreakdown && dashboard.dispositionBreakdown.length > 0) {
          <section class="disposition-section">
            <div class="widget-header">
              <div>
                <h3 class="widget-title">Call Dispositions & Outcomes</h3>
                <p class="widget-subtitle">Business results categorized across closed interactions.</p>
              </div>
              @if (dashboard.pendingFollowUpsCount !== undefined) {
                <div class="followup-badge">
                  <span class="badge-icon">⏰</span>
                  <span><strong>{{ dashboard.pendingFollowUpsCount }}</strong> Pending Follow-Ups</span>
                </div>
              }
            </div>

            <div class="disposition-bars">
              @for (item of dashboard.dispositionBreakdown; track item.dispositionId) {
                <div class="disp-bar-item">
                  <div class="bar-meta">
                    <span class="bar-name">{{ item.dispositionName }}</span>
                    <span class="bar-stats">
                      <strong>{{ item.callCount }}</strong> calls ({{ item.percentage | number:'1.1-1' }}%)
                    </span>
                  </div>
                  <div class="bar-track">
                    <div class="bar-fill" [style.width.%]="item.percentage"></div>
                  </div>
                </div>
              }
            </div>
          </section>
        }
      }
    </div>
  `,
  styles: [`
    .dashboard-shell {
      display: flex;
      flex-direction: column;
      gap: 24px;
      padding-bottom: 40px;
    }

    /* Top Command Bar */
    .command-bar {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      flex-wrap: wrap;
      gap: 16px;
      background: #ffffff;
      border: 1px solid #e2e8f0;
      border-radius: 16px;
      padding: 20px 24px;
      box-shadow: 0 1px 3px rgba(15, 23, 42, 0.04);
    }
    .command-info {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }
    .pulse-tag {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      font-size: 11.5px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.06em;
      color: #059669;
      background: #ecfdf5;
      padding: 3px 8px;
      border-radius: 6px;
      width: fit-content;
    }
    .pulse-indicator {
      width: 7px;
      height: 7px;
      border-radius: 50%;
      background: #10b981;
      animation: pulse-dot 1.8s infinite;
    }
    .command-title {
      margin: 0;
      font-size: 22px;
      font-weight: 800;
      color: #0f172a;
      letter-spacing: -0.02em;
    }
    .command-subtitle {
      margin: 0;
      font-size: 13.5px;
      color: #64748b;
    }

    .command-actions {
      display: flex;
      align-items: center;
      gap: 16px;
      flex-wrap: wrap;
    }
    .auto-refresh-toggle {
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      padding: 6px 12px;
      border-radius: 10px;
    }
    .toggle-label {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 12.5px;
      font-weight: 500;
      color: #334155;
      cursor: pointer;
      user-select: none;
    }
    .toggle-label input {
      accent-color: #2563eb;
      cursor: pointer;
    }

    .refresh-meta {
      display: flex;
      align-items: center;
      gap: 12px;
    }
    .last-sync {
      font-size: 12px;
      color: #94a3b8;
      font-variant-numeric: tabular-nums;
    }
    .refresh-btn {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      background: #0f172a;
      color: #ffffff;
      border: 1px solid #0f172a;
      padding: 7px 14px;
      border-radius: 10px;
      font-size: 13px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.15s ease;
    }
    .refresh-btn:hover:not(:disabled) {
      background: #1e293b;
    }
    .refresh-btn:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }
    .refresh-icon {
      font-size: 14px;
      display: inline-block;
    }
    .refresh-icon.spinning {
      animation: spin 1s linear infinite;
    }

    /* Error Banner */
    .error-banner {
      background: #fef2f2;
      border: 1px solid #fecaca;
      border-radius: 12px;
      padding: 14px 18px;
      display: flex;
      justify-content: space-between;
      align-items: center;
      color: #991b1b;
      font-size: 13.5px;
    }
    .error-content {
      display: flex;
      align-items: center;
      gap: 10px;
    }
    .retry-btn {
      background: #ffffff;
      border: 1px solid #dc2626;
      color: #dc2626;
      padding: 4px 12px;
      border-radius: 6px;
      font-weight: 600;
      font-size: 12px;
      cursor: pointer;
    }

    /* Loading State */
    .loading-state {
      background: #ffffff;
      border: 1px solid #e2e8f0;
      border-radius: 16px;
      padding: 60px 20px;
      text-align: center;
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 14px;
      color: #64748b;
      font-size: 14px;
    }
    .loading-spinner {
      width: 32px;
      height: 32px;
      border: 3px solid #e2e8f0;
      border-top-color: #2563eb;
      border-radius: 50%;
      animation: spin 0.8s linear infinite;
    }

    /* KPI Ribbon (10 Core Metrics Grid) */
    .kpi-ribbon {
      display: grid;
      grid-template-columns: repeat(5, 1fr);
      gap: 14px;
    }
    @media (max-width: 1280px) {
      .kpi-ribbon {
        grid-template-columns: repeat(3, 1fr);
      }
    }
    @media (max-width: 768px) {
      .kpi-ribbon {
        grid-template-columns: repeat(2, 1fr);
      }
    }
    @media (max-width: 480px) {
      .kpi-ribbon {
        grid-template-columns: 1fr;
      }
    }

    /* Period Statistics Grid (Daily, Weekly, Monthly) */
    .period-stats-grid {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 16px;
    }
    @media (max-width: 992px) {
      .period-stats-grid {
        grid-template-columns: 1fr;
      }
    }

    /* Trend Section */
    .trend-section {
      width: 100%;
    }

    /* Operations Split Section (Agents + Queue) */
    .operations-split {
      display: grid;
      grid-template-columns: 2fr 1fr;
      gap: 18px;
      align-items: start;
    }
    @media (max-width: 1024px) {
      .operations-split {
        grid-template-columns: 1fr;
      }
    }

    /* Dispositions Widget */
    .disposition-section {
      background: #ffffff;
      border: 1px solid #e2e8f0;
      border-radius: 16px;
      padding: 24px;
      box-shadow: 0 1px 3px rgba(15, 23, 42, 0.04);
    }
    .widget-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 16px;
      margin-bottom: 20px;
    }
    .widget-title {
      margin: 0;
      font-size: 17px;
      font-weight: 800;
      color: #0f172a;
    }
    .widget-subtitle {
      margin: 2px 0 0;
      font-size: 13px;
      color: #64748b;
    }
    .followup-badge {
      display: flex;
      align-items: center;
      gap: 6px;
      background: #fef3c7;
      border: 1px solid #fde68a;
      color: #92400e;
      padding: 6px 12px;
      border-radius: 9999px;
      font-size: 12.5px;
    }
    .disposition-bars {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 16px 28px;
    }
    @media (max-width: 768px) {
      .disposition-bars {
        grid-template-columns: 1fr;
      }
    }
    .disp-bar-item {
      display: flex;
      flex-direction: column;
      gap: 6px;
    }
    .bar-meta {
      display: flex;
      justify-content: space-between;
      font-size: 13px;
    }
    .bar-name {
      font-weight: 600;
      color: #334155;
    }
    .bar-stats {
      color: #64748b;
      font-size: 12.5px;
    }
    .bar-track {
      height: 8px;
      background: #f1f5f9;
      border-radius: 9999px;
      overflow: hidden;
    }
    .bar-fill {
      height: 100%;
      background: linear-gradient(90deg, #0284c7, #38bdf8);
      border-radius: 9999px;
      transition: width 0.4s ease;
    }

    @keyframes spin {
      from { transform: rotate(0deg); }
      to { transform: rotate(360deg); }
    }
    @keyframes pulse-dot {
      0% { transform: scale(0.9); opacity: 0.7; }
      50% { transform: scale(1.3); opacity: 1; }
      100% { transform: scale(0.9); opacity: 0.7; }
    }
  `]
})
export class DashboardComponent implements OnInit, OnDestroy {
  private readonly reportsService = inject(ReportsService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly realtime = inject(CallCenterRealtimeService);
  private readonly cdr = inject(ChangeDetectorRef);

  dashboard: OperationsDashboard | null = null;
  loading = false;
  error = false;
  lastRefreshedAt: Date = new Date();
  autoRefreshEnabled = true;

  private pollIntervalId: any = null;
  private realtimeSub?: Subscription;

  ngOnInit(): void {
    // If agent-only role, navigate to agent workspace
    if (this.auth.hasRole('Agent') && !this.auth.hasRole('Admin') && !this.auth.hasRole('Supervisor')) {
      void this.router.navigate(['/agent-dashboard']);
      return;
    }

    this.load(true);
    this.initRealtime();
    this.startAutoRefresh();
  }

  ngOnDestroy(): void {
    this.stopAutoRefresh();
    this.realtimeSub?.unsubscribe();
  }

  load(isInitial: boolean = false): void {
    if (isInitial && !this.dashboard) {
      this.loading = true;
    }
    this.error = false;

    this.reportsService
      .getDashboard()
      .pipe(
        finalize(() => {
          this.loading = false;
          this.cdr.markForCheck();
        })
      )
      .subscribe({
        next: (data) => {
          this.dashboard = data;
          this.lastRefreshedAt = new Date();
          this.cdr.markForCheck();
        },
        error: () => {
          this.error = true;
          this.cdr.markForCheck();
        }
      });
  }

  toggleAutoRefresh(): void {
    this.autoRefreshEnabled = !this.autoRefreshEnabled;
    if (this.autoRefreshEnabled) {
      this.startAutoRefresh();
    } else {
      this.stopAutoRefresh();
    }
  }

  private startAutoRefresh(): void {
    this.stopAutoRefresh();
    this.pollIntervalId = setInterval(() => {
      if (this.autoRefreshEnabled) {
        this.load(false);
      }
    }, 30000);
  }

  private stopAutoRefresh(): void {
    if (this.pollIntervalId) {
      clearInterval(this.pollIntervalId);
      this.pollIntervalId = null;
    }
  }

  private initRealtime(): void {
    this.realtime.connect();
    this.realtimeSub = this.realtime.events$
      .pipe(debounceTime(1500))
      .subscribe(() => {
        // Trigger live refresh when events occur in call center
        this.load(false);
      });
  }

  formatDuration(seconds: number): string {
    if (!seconds || seconds <= 0) return '00:00';
    const mins = Math.floor(seconds / 60);
    const secs = Math.round(seconds % 60);
    return `${String(mins).padStart(2, '0')}:${String(secs).padStart(2, '0')}`;
  }
}
