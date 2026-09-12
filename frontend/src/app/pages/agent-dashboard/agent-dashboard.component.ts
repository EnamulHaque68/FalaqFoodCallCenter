import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  DestroyRef,
  OnDestroy,
  OnInit,
  inject
} from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize, interval, Subscription } from 'rxjs';
import { AgentDashboardService } from '../../core/services/agent-dashboard.service';
import { CallCenterRealtimeService } from '../../core/services/call-center-realtime.service';
import { TelephonyService } from '../../core/services/telephony.service';
import { IncomingCallSessionService } from '../../core/services/incoming-call-session.service';
import { TwilioVoiceService } from '../../core/services/twilio-voice.service';
import {
  AgentDashboard,
  AgentStatus,
  CallDirection,
  CallStatus,
  QueueItemSummary
} from '../../core/models/agent-dashboard.models';

@Component({
  selector: 'app-agent-dashboard',
  standalone: true,
  imports: [CommonModule, DatePipe],
  template: `
    <div class="agent-dashboard-page">
      <!-- Header -->
      <div class="page-header agent-header">
        <div>
          <span class="eyebrow">Personal Workspace</span>
          <h2>Agent Dashboard</h2>
          <p>Real-time call handling, personal performance KPIs, and inbound queue monitoring.</p>
        </div>
        @if (dashboard) {
          <div class="agent-identity">
            <div class="avatar">{{ initials(dashboard.displayName) }}</div>
            <div class="identity-text">
              <strong>{{ dashboard.displayName }}</strong>
              <div class="identity-meta">
                <span class="emp-code">{{ dashboard.employeeCode }}</span>
                @if (dashboard.team) {
                  <span class="team-tag">{{ dashboard.team }}</span>
                }
              </div>
            </div>
          </div>
        }
      </div>

      <!-- Error Toast -->
      @if (errorMessage) {
        <div class="error-banner">
          <span>{{ errorMessage }}</span>
          <button type="button" class="retry-btn" (click)="loadDashboard()">Retry</button>
        </div>
      }

      <!-- Loading State -->
      @if (loading && !dashboard) {
        <div class="dashboard-loading card">
          <div class="spinner"></div>
          <span>Loading agent workspace...</span>
        </div>
      } @else if (dashboard) {
        <!-- Status Switcher Card -->
        <section class="card status-panel">
          <div class="current-status-group">
            <span class="section-label">My Availability</span>
            <div class="current-status">
              <span class="status-dot-pulse" [ngClass]="statusClass(dashboard.status)"></span>
              <div>
                <strong class="status-title" [ngClass]="statusClass(dashboard.status)">
                  {{ statusLabel(dashboard.status) }}
                </strong>
                <small class="status-desc">{{ statusDescription(dashboard.status) }}</small>
              </div>
            </div>
          </div>

          <div class="status-actions">
            <span class="section-label">Switch Status</span>
            <div class="status-buttons">
              @for (st of statuses; track st) {
                <button
                  type="button"
                  [class.selected]="dashboard.status === st"
                  [disabled]="statusUpdating || dashboard.status === st"
                  (click)="changeStatus(st)">
                  <span class="status-dot" [ngClass]="statusClass(st)"></span>
                  {{ statusLabel(st) }}
                </button>
              }
            </div>
          </div>
        </section>

        <!-- KPI Metrics Grid (Personal Data Only) -->
        <section class="kpi-grid">
          <div class="kpi-card card">
            <div class="kpi-header">
              <span class="kpi-label">Today's Calls</span>
              <span class="kpi-icon">📞</span>
            </div>
            <strong class="kpi-value">{{ dashboard.todaysCallsCount }}</strong>
            <small class="kpi-hint">Assigned to you today</small>
          </div>

          <div class="kpi-card card">
            <div class="kpi-header">
              <span class="kpi-label">Completed</span>
              <span class="kpi-icon text-success">✓</span>
            </div>
            <strong class="kpi-value text-success">{{ dashboard.completedCallsCount }}</strong>
            <small class="kpi-hint">Successfully handled</small>
          </div>

          <div class="kpi-card card">
            <div class="kpi-header">
              <span class="kpi-label">Missed / Failed</span>
              <span class="kpi-icon text-danger">✕</span>
            </div>
            <strong class="kpi-value text-danger">{{ dashboard.missedCallsCount }}</strong>
            <small class="kpi-hint">Abandoned or missed</small>
          </div>

          <div class="kpi-card card">
            <div class="kpi-header">
              <span class="kpi-label">Average Duration</span>
              <span class="kpi-icon text-blue">⏱</span>
            </div>
            <strong class="kpi-value text-blue">{{ formatDuration(dashboard.averageDurationSeconds) }}</strong>
            <small class="kpi-hint">Talk time average</small>
          </div>

          <div class="kpi-card card queue-kpi" [class.has-queue]="dashboard.queueCount > 0">
            <div class="kpi-header">
              <span class="kpi-label">Incoming Queue</span>
              <span class="kpi-icon text-purple">⚡</span>
            </div>
            <strong class="kpi-value text-purple">{{ dashboard.queueCount }}</strong>
            <small class="kpi-hint">
              {{ dashboard.queueCount === 1 ? '1 call waiting' : dashboard.queueCount + ' calls waiting' }}
            </small>
          </div>
        </section>

        <!-- Main Workspace Split: Left (Active Call & Queue) / Right (Recent Calls) -->
        <div class="workspace-grid">
          <!-- LEFT COLUMN: Active Call & Queue -->
          <div class="workspace-left">
            <!-- Active Call Banner -->
            <article class="card active-call-card" [class.has-call]="!!dashboard.currentCall">
              <div class="card-heading">
                <div>
                  <span class="section-label">Current Workspace</span>
                  <h3>Active Call</h3>
                </div>
                <span class="live-badge" [class.ringing]="dashboard.currentCall?.status === CallStatus.Ringing" [class.active]="dashboard.currentCall?.status === CallStatus.Connected">
                  {{ dashboard.currentCall ? (dashboard.currentCall.status === CallStatus.Ringing ? '🔔 RINGING' : '🔴 LIVE') : 'IDLE' }}
                </span>
              </div>

              @if (dashboard.currentCall) {
                <div class="active-call-content">
                  <div class="call-hero">
                    <div class="call-icon-large" [class.ringing]="dashboard.currentCall.status === CallStatus.Ringing">
                      {{ dashboard.currentCall.direction === CallDirection.Inbound ? '📥' : '📤' }}
                    </div>
                    <div>
                      <h4 class="caller-phone">{{ dashboard.currentCall.phoneNumber }}</h4>
                      <span class="caller-meta">
                        {{ callDirectionLabel(dashboard.currentCall.direction) }} ·
                        {{ callStatusLabel(dashboard.currentCall.status) }}
                      </span>
                    </div>
                  </div>

                  <div class="active-timer-box">
                    <span class="timer-label">Call Duration</span>
                    <strong class="timer-digits">{{ activeCallDurationFormatted }}</strong>
                  </div>

                  <div class="call-details-grid">
                    <div>
                      <span>Customer Name</span>
                      <strong>{{ dashboard.currentCall.customerName || 'Direct Caller' }}</strong>
                    </div>
                    <div>
                      <span>Started At</span>
                      <strong>{{ dashboard.currentCall.startedAt | date:'shortTime' }}</strong>
                    </div>
                    @if (dashboard.currentCall.dispositionName) {
                      <div>
                        <span>Disposition</span>
                        <strong>{{ dashboard.currentCall.dispositionName }}</strong>
                      </div>
                    }
                  </div>

                  <div class="call-actions-row">
                    @if (dashboard.currentCall.status === CallStatus.Ringing) {
                      <button type="button" class="btn-quick-answer" (click)="answerIncomingCallFromDashboard()">
                        📞 Answer Call
                      </button>
                      <button type="button" class="btn-quick-reject" (click)="rejectIncomingCallFromDashboard()">
                        🚫 Decline
                      </button>
                    }
                    <button type="button" class="btn-open-call" (click)="openActiveCallWorkspace()">
                      Open Full Call Workspace →
                    </button>
                  </div>
                </div>
              } @else {
                <div class="empty-call-state">
                  <div class="idle-icon">🎧</div>
                  <strong>Workspace Ready</strong>
                  <span>No active call. When an inbound caller is assigned to you, details and controls will display here.</span>
                  @if (dashboard.status !== AgentStatus.Available) {
                    <button type="button" class="btn-go-available" (click)="changeStatus(AgentStatus.Available)">
                      Go Available Now
                    </button>
                  }
                </div>
              }
            </article>

            <!-- Incoming Queue Card -->
            <article class="card queue-card">
              <div class="card-heading">
                <div>
                  <span class="section-label">Live Inbound Feed</span>
                  <h3>Incoming Queue ({{ dashboard.queueCount }})</h3>
                </div>
                <span class="queue-status-indicator" [class.busy]="dashboard.queueCount > 0">
                  {{ dashboard.queueCount > 0 ? 'Waiters in Queue' : 'Queue Clear' }}
                </span>
              </div>

              @if (dashboard.incomingQueue.length > 0) {
                <div class="queue-list">
                  @for (q of dashboard.incomingQueue; track q.callId) {
                    <div class="queue-item">
                      <div class="queue-pos-badge">#{{ q.position }}</div>
                      <div class="queue-info">
                        <strong>{{ q.phoneNumber }}</strong>
                        <small>{{ q.queueName || 'General Queue' }} · {{ formatWaitTime(q.enqueuedAt) }} waiting</small>
                      </div>
                      <span class="queue-wait-chip">In Queue</span>
                    </div>
                  }
                </div>
              } @else {
                <div class="empty-queue-state">
                  <span>⚡</span>
                  <p>Inbound queue is clear. Calls will ring available agents immediately.</p>
                </div>
              }
            </article>
          </div>

          <!-- RIGHT COLUMN: Recent Personal Calls -->
          <div class="workspace-right">
            <article class="card recent-calls-card">
              <div class="card-heading">
                <div>
                  <span class="section-label">Call History</span>
                  <h3>My Recent Calls</h3>
                </div>
                <button type="button" class="view-history-btn" (click)="navigateToCallHistory()">
                  View Full History →
                </button>
              </div>

              @if (dashboard.recentCalls.length > 0) {
                <div class="recent-table-wrapper">
                  <table class="recent-table">
                    <thead>
                      <tr>
                        <th>Direction</th>
                        <th>Contact / Phone</th>
                        <th>Status</th>
                        <th>Duration</th>
                        <th>Time</th>
                      </tr>
                    </thead>
                    <tbody>
                      @for (call of dashboard.recentCalls; track call.id) {
                        <tr class="recent-call-row" (click)="viewCallDetails(call.id)">
                          <td>
                            <span class="dir-icon-pill" [class.outbound]="call.direction === CallDirection.Outbound">
                              {{ call.direction === CallDirection.Inbound ? '↙ In' : '↗ Out' }}
                            </span>
                          </td>
                          <td>
                            <strong class="phone-link">{{ call.phoneNumber }}</strong>
                            <small class="customer-sub">{{ call.customerName || 'Customer' }}</small>
                          </td>
                          <td>
                            <span class="status-pill" [ngClass]="'call-status-' + call.status">
                              {{ callStatusLabel(call.status) }}
                            </span>
                          </td>
                          <td>
                            <span class="duration-text">{{ formatDuration(call.durationSeconds) }}</span>
                          </td>
                          <td>
                            <small class="time-text">{{ call.startedAt | date:'shortTime' }}</small>
                          </td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </div>
              } @else {
                <div class="empty-recent-state">
                  <span>📜</span>
                  <strong>No calls logged today</strong>
                  <p>Calls handled during your current session will appear in this log.</p>
                </div>
              }
            </article>
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    .agent-dashboard-page {
      max-width: 1400px;
      margin: 0 auto;
      display: flex;
      flex-direction: column;
      gap: 20px;
    }

    .page-header {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: 16px;
    }

    .agent-identity {
      display: flex;
      align-items: center;
      gap: 12px;
      background: #fff;
      border: 1px solid #e2e8f0;
      border-radius: 12px;
      padding: 10px 16px;
      box-shadow: 0 1px 3px rgba(0,0,0,0.02);
    }
    .agent-identity .avatar {
      width: 42px;
      height: 42px;
      border-radius: 10px;
      background: #0d1628;
      color: #fff;
      display: grid;
      place-items: center;
      font-size: 14px;
      font-weight: 800;
    }
    .identity-text strong { display: block; font-size: 14px; color: #0f172a; }
    .identity-meta { display: flex; align-items: center; gap: 6px; margin-top: 3px; }
    .emp-code {
      font-family: ui-monospace, monospace;
      font-size: 11px;
      font-weight: 700;
      color: #475569;
      background: #f1f5f9;
      padding: 2px 6px;
      border-radius: 4px;
    }
    .team-tag {
      font-size: 11px;
      font-weight: 600;
      color: #4338ca;
      background: #e0e7ff;
      padding: 2px 7px;
      border-radius: 4px;
    }

    .card {
      background: #fff;
      border: 1px solid #e2e8f0;
      border-radius: 14px;
      box-shadow: 0 1px 3px rgba(0,0,0,0.02);
    }

    /* Status Switcher Panel */
    .status-panel {
      padding: 20px 24px;
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 24px;
      flex-wrap: wrap;
    }
    .section-label {
      text-transform: uppercase;
      letter-spacing: 0.08em;
      font-size: 10px;
      font-weight: 800;
      color: #64748b;
      display: block;
      margin-bottom: 6px;
    }
    .current-status {
      display: flex;
      align-items: center;
      gap: 12px;
    }
    .status-dot-pulse {
      width: 14px;
      height: 14px;
      border-radius: 50%;
      display: inline-block;
      position: relative;
    }
    .status-dot-pulse.available {
      background: #22c55e;
      box-shadow: 0 0 0 4px rgba(34,197,94,0.25);
      animation: pulseGreen 2s infinite;
    }
    .status-dot-pulse.busy { background: #3b82f6; box-shadow: 0 0 0 4px rgba(59,130,246,0.2); }
    .status-dot-pulse.away { background: #f59e0b; box-shadow: 0 0 0 4px rgba(245,158,11,0.25); }
    .status-dot-pulse.wrapup { background: #8b5cf6; box-shadow: 0 0 0 4px rgba(139,92,246,0.2); }
    .status-dot-pulse.offline { background: #94a3b8; }
    @keyframes pulseGreen {
      0% { box-shadow: 0 0 0 0 rgba(34,197,94,0.4); }
      70% { box-shadow: 0 0 0 8px rgba(34,197,94,0); }
      100% { box-shadow: 0 0 0 0 rgba(34,197,94,0); }
    }

    .status-title { font-size: 20px; font-weight: 750; display: block; }
    .status-title.available { color: #15803d; }
    .status-title.busy { color: #1d4ed8; }
    .status-title.away { color: #b45309; }
    .status-title.wrapup { color: #6d28d9; }
    .status-title.offline { color: #475569; }
    .status-desc { font-size: 12px; color: #64748b; }

    .status-actions {
      display: flex;
      flex-direction: column;
      align-items: flex-end;
    }
    .status-buttons {
      display: flex;
      gap: 8px;
      flex-wrap: wrap;
    }
    .status-buttons button {
      border: 1px solid #cbd5e1;
      background: #fff;
      border-radius: 8px;
      padding: 9px 14px;
      display: flex;
      align-items: center;
      gap: 7px;
      color: #334155;
      font-size: 13px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.12s;
    }
    .status-buttons button:hover:not(:disabled) { border-color: #0d1628; background: #f8fafc; }
    .status-buttons button.selected { background: #0d1628; border-color: #0d1628; color: #fff; }
    .status-buttons button:disabled { opacity: 0.6; cursor: default; }

    .status-dot { width: 8px; height: 8px; border-radius: 50%; display: inline-block; }
    .status-dot.available { background: #22c55e; }
    .status-dot.busy { background: #3b82f6; }
    .status-dot.away { background: #f59e0b; }
    .status-dot.wrapup { background: #8b5cf6; }
    .status-dot.offline { background: #94a3b8; }

    /* KPI Metrics Ribbon */
    .kpi-grid {
      display: grid;
      grid-template-columns: repeat(5, minmax(0, 1fr));
      gap: 14px;
    }
    @media (max-width: 1100px) { .kpi-grid { grid-template-columns: repeat(3, 1fr); } }
    @media (max-width: 650px) { .kpi-grid { grid-template-columns: 1fr; } }

    .kpi-card {
      padding: 16px 18px;
      display: flex;
      flex-direction: column;
    }
    .kpi-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
    }
    .kpi-label {
      font-size: 11px;
      font-weight: 750;
      color: #64748b;
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }
    .kpi-icon { font-size: 16px; opacity: 0.85; }
    .kpi-value { font-size: 26px; font-weight: 800; margin: 4px 0 2px; color: #0f172a; }
    .kpi-hint { font-size: 11px; color: #94a3b8; }
    .text-success { color: #16a34a !important; }
    .text-danger { color: #dc2626 !important; }
    .text-blue { color: #2563eb !important; }
    .text-purple { color: #7c3aed !important; }

    .queue-kpi.has-queue {
      border-color: #c4b5fd;
      background: #faf5ff;
    }

    /* Workspace Columns */
    .workspace-grid {
      display: grid;
      grid-template-columns: 1.15fr 1.35fr;
      gap: 18px;
      align-items: start;
    }
    @media (max-width: 1000px) { .workspace-grid { grid-template-columns: 1fr; } }

    .workspace-left {
      display: flex;
      flex-direction: column;
      gap: 18px;
    }

    .card-heading {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: 12px;
      margin-bottom: 16px;
    }
    .card-heading h3 { font-size: 17px; margin: 2px 0 0; }

    /* Active Call Card */
    .active-call-card { padding: 22px; }
    .active-call-card.has-call { border-color: #bfdbfe; background: #fdfefe; }
    .live-badge {
      font-size: 10px;
      font-weight: 800;
      letter-spacing: 0.08em;
      padding: 5px 9px;
      border-radius: 6px;
      background: #f1f5f9;
      color: #64748b;
    }
    .live-badge.ringing { background: #fef3c7; color: #b45309; animation: pulseAmber 1.5s infinite; }
    .live-badge.active { background: #fee2e2; color: #b91c1c; animation: pulseRed 2s infinite; }
    @keyframes pulseAmber { 0%, 100% { opacity: 1; } 50% { opacity: 0.5; } }
    @keyframes pulseRed { 0%, 100% { opacity: 1; } 50% { opacity: 0.6; } }

    .active-call-content {
      display: flex;
      flex-direction: column;
      gap: 18px;
    }
    .call-hero {
      display: flex;
      align-items: center;
      gap: 14px;
      padding-bottom: 14px;
      border-bottom: 1px solid #f1f5f9;
    }
    .call-icon-large {
      width: 48px;
      height: 48px;
      border-radius: 12px;
      background: #eff6ff;
      display: grid;
      place-items: center;
      font-size: 22px;
    }
    .call-icon-large.ringing { animation: shake 0.8s infinite; }
    @keyframes shake {
      0%, 100% { transform: rotate(0); }
      25% { transform: rotate(-10deg); }
      75% { transform: rotate(10deg); }
    }
    .caller-phone { font-size: 22px; margin: 0; font-weight: 800; color: #0f172a; }
    .caller-meta { font-size: 12px; color: #64748b; display: block; margin-top: 2px; }

    .active-timer-box {
      background: #0d1628;
      color: #fff;
      border-radius: 10px;
      padding: 12px 18px;
      display: flex;
      align-items: center;
      justify-content: space-between;
    }
    .timer-label { font-size: 12px; color: #94a3b8; text-transform: uppercase; font-weight: 700; }
    .timer-digits { font-family: ui-monospace, monospace; font-size: 22px; font-weight: 800; letter-spacing: 0.05em; }

    .call-details-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 10px;
      background: #f8fafc;
      padding: 12px;
      border-radius: 8px;
    }
    .call-details-grid span { font-size: 11px; color: #64748b; font-weight: 600; text-transform: uppercase; }
    .call-details-grid strong { font-size: 13px; color: #1e293b; display: block; margin-top: 2px; }

    .btn-open-call {
      width: 100%;
      background: #0d1628;
      color: #fff;
      border: none;
      padding: 12px;
      border-radius: 8px;
      font-weight: 700;
      font-size: 13px;
      cursor: pointer;
      transition: background 0.15s;
    }
    .btn-open-call:hover {
      background: #1e293b;
    }
    .btn-quick-answer {
      background: #16a34a;
      color: #fff;
      border: none;
      padding: 9px 16px;
      border-radius: 8px;
      font-size: 13px;
      font-weight: 700;
      cursor: pointer;
      display: inline-flex;
      align-items: center;
      gap: 6px;
      transition: background 0.15s ease;
    }
    .btn-quick-answer:hover { background: #15803d; }
    .btn-quick-reject {
      background: #fee2e2;
      color: #dc2626;
      border: 1px solid #fca5a5;
      padding: 9px 14px;
      border-radius: 8px;
      font-size: 13px;
      font-weight: 700;
      cursor: pointer;
      display: inline-flex;
      align-items: center;
      gap: 6px;
      transition: background 0.15s ease;
    }
    .btn-quick-reject:hover { background: #fecaca; }

    .empty-call-state {
      padding: 36px 20px;
      text-align: center;
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 8px;
      color: #64748b;
    }
    .idle-icon { font-size: 38px; opacity: 0.7; margin-bottom: 4px; }
    .empty-call-state strong { font-size: 15px; color: #1e293b; }
    .empty-call-state span { font-size: 12px; max-width: 320px; }
    .btn-go-available {
      margin-top: 10px;
      background: #15803d;
      color: #fff;
      border: none;
      padding: 8px 16px;
      border-radius: 6px;
      font-weight: 650;
      font-size: 12px;
      cursor: pointer;
    }
    .btn-go-available:hover { background: #166534; }

    /* Queue Card */
    .queue-card { padding: 22px; }
    .queue-status-indicator {
      font-size: 11px;
      font-weight: 700;
      padding: 4px 8px;
      border-radius: 6px;
      background: #f1f5f9;
      color: #64748b;
    }
    .queue-status-indicator.busy { background: #faf5ff; color: #7c3aed; border: 1px solid #e9d5ff; }

    .queue-list { display: flex; flex-direction: column; gap: 8px; }
    .queue-item {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 10px 14px;
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      border-radius: 8px;
    }
    .queue-pos-badge {
      width: 28px;
      height: 28px;
      border-radius: 50%;
      background: #7c3aed;
      color: #fff;
      display: grid;
      place-items: center;
      font-size: 11px;
      font-weight: 800;
    }
    .queue-info { flex: 1; }
    .queue-info strong { display: block; font-size: 13px; color: #0f172a; }
    .queue-info small { color: #64748b; font-size: 11px; }
    .queue-wait-chip {
      font-size: 11px;
      font-weight: 650;
      padding: 3px 8px;
      border-radius: 12px;
      background: #ede9fe;
      color: #6d28d9;
    }
    .empty-queue-state {
      padding: 24px;
      text-align: center;
      color: #64748b;
      font-size: 12px;
    }
    .empty-queue-state span { font-size: 24px; display: block; margin-bottom: 6px; opacity: 0.6; }

    /* Recent Personal Calls */
    .recent-calls-card { padding: 22px; }
    .view-history-btn {
      background: none;
      border: none;
      color: #2563eb;
      font-size: 12px;
      font-weight: 650;
      cursor: pointer;
    }
    .view-history-btn:hover { text-decoration: underline; }

    .recent-table-wrapper { overflow-x: auto; margin-top: 6px; }
    .recent-table { width: 100%; border-collapse: collapse; text-align: left; font-size: 12px; }
    .recent-table th {
      padding: 10px 12px;
      color: #64748b;
      font-size: 11px;
      text-transform: uppercase;
      font-weight: 700;
      border-bottom: 1px solid #e2e8f0;
      background: #f8fafc;
    }
    .recent-table td {
      padding: 12px;
      border-bottom: 1px solid #f1f5f9;
      vertical-align: middle;
    }
    .recent-call-row { cursor: pointer; transition: background 0.1s; }
    .recent-call-row:hover td { background: #f8fafc; }
    .phone-link { color: #0f172a; display: block; }
    .customer-sub { color: #64748b; display: block; font-size: 11px; }

    .dir-icon-pill {
      display: inline-block;
      padding: 2px 6px;
      border-radius: 4px;
      font-size: 10px;
      font-weight: 750;
      background: #ecfdf3;
      color: #15803d;
    }
    .dir-icon-pill.outbound { background: #eff6ff; color: #2563eb; }

    .status-pill {
      display: inline-block;
      padding: 2px 7px;
      border-radius: 6px;
      font-size: 10px;
      font-weight: 700;
    }
    .call-status-4 { background: #dcfce7; color: #15803d; } /* Completed */
    .call-status-3 { background: #dbeafe; color: #1d4ed8; } /* Connected */
    .call-status-2 { background: #fef3c7; color: #b45309; } /* Ringing */
    .call-status-5 { background: #fee2e2; color: #b91c1c; } /* Abandoned */

    .empty-recent-state {
      padding: 40px 20px;
      text-align: center;
      color: #64748b;
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 6px;
    }
    .empty-recent-state span { font-size: 32px; opacity: 0.6; }
    .empty-recent-state strong { font-size: 14px; color: #1e293b; }
    .empty-recent-state p { font-size: 12px; margin: 0; }

    .error-banner {
      background: #fef2f2;
      border: 1px solid #fecaca;
      color: #991b1b;
      padding: 12px 16px;
      border-radius: 8px;
      display: flex;
      align-items: center;
      justify-content: space-between;
      font-size: 13px;
    }
    .retry-btn {
      background: #fff;
      border: 1px solid #fca5a5;
      color: #991b1b;
      padding: 4px 10px;
      border-radius: 6px;
      font-size: 12px;
      cursor: pointer;
    }

    .dashboard-loading {
      padding: 60px;
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 12px;
      color: #64748b;
    }
    .spinner {
      width: 28px;
      height: 28px;
      border: 3px solid #e2e8f0;
      border-top-color: #0d1628;
      border-radius: 50%;
      animation: spin 0.7s linear infinite;
    }
    @keyframes spin { to { transform: rotate(360deg); } }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AgentDashboardComponent implements OnInit, OnDestroy {
  private readonly service = inject(AgentDashboardService);
  private readonly realtime = inject(CallCenterRealtimeService);
  private readonly telephony = inject(TelephonyService);
  private readonly sessionService = inject(IncomingCallSessionService);
  public readonly voiceService = inject(TwilioVoiceService);
  private readonly router = inject(Router);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly destroyRef = inject(DestroyRef);

  readonly AgentStatus = AgentStatus;
  readonly CallDirection = CallDirection;
  readonly CallStatus = CallStatus;

  readonly statuses: AgentStatus[] = [
    AgentStatus.Available,
    AgentStatus.Busy,
    AgentStatus.Away,
    AgentStatus.Offline,
    AgentStatus.WrapUp
  ];

  dashboard: AgentDashboard | null = null;
  loading = false;
  statusUpdating = false;
  errorMessage = '';

  // Live timer for active call
  activeCallDurationFormatted = '00:00';
  private timerSub?: Subscription;

  ngOnInit(): void {
    this.loadDashboard();
    this.realtime.connect();

    // Listen for real-time events to update dashboard state automatically
    this.realtime.events$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(event => {
        if (
          event.type === 'agent-status' ||
          event.type === 'call-status' ||
          event.type === 'queue-updated' ||
          event.type === 'incoming-call'
        ) {
          this.loadDashboard(true);
        }
      });

    // Tick duration timer every second
    this.timerSub = interval(1000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.updateActiveCallTimer());
  }

  ngOnDestroy(): void {
    this.timerSub?.unsubscribe();
  }

  loadDashboard(silent = false): void {
    if (!silent) this.loading = true;
    this.errorMessage = '';
    this.cdr.markForCheck();

    this.service
      .getMyDashboard()
      .pipe(
        finalize(() => {
          this.loading = false;
          this.cdr.markForCheck();
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: dashboard => {
          this.dashboard = dashboard;
          this.updateActiveCallTimer();
          this.cdr.markForCheck();
        },
        error: error => {
          this.errorMessage = error?.error?.message || 'Unable to load your agent workspace.';
          this.cdr.markForCheck();
        }
      });
  }

  changeStatus(status: AgentStatus): void {
    if (!this.dashboard || this.statusUpdating) return;
    this.statusUpdating = true;
    this.errorMessage = '';
    this.cdr.markForCheck();

    this.service
      .updateMyStatus(this.dashboard.agentId, status)
      .pipe(
        finalize(() => {
          this.statusUpdating = false;
          this.cdr.markForCheck();
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: agent => {
          if (this.dashboard) {
            this.dashboard = { ...this.dashboard, status: agent.status };
          }
          this.cdr.markForCheck();
        },
        error: error => {
          this.errorMessage = error?.error?.message || 'Failed to update availability status.';
          this.cdr.markForCheck();
        }
      });
  }

  private updateActiveCallTimer(): void {
    if (!this.dashboard?.currentCall) {
      this.activeCallDurationFormatted = '00:00';
      return;
    }

    const start = new Date(this.dashboard.currentCall.startedAt).getTime();
    const now = Date.now();
    const diffSec = Math.max(0, Math.floor((now - start) / 1000));
    const mins = Math.floor(diffSec / 60);
    const secs = diffSec % 60;
    this.activeCallDurationFormatted = `${mins.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
    this.cdr.markForCheck();
  }

  openActiveCallWorkspace(): void {
    if (!this.dashboard?.currentCall) return;
    void this.router.navigate(['/active-call']);
  }

  answerIncomingCallFromDashboard(): void {
    const call = this.dashboard?.currentCall;
    if (!call) return;

    this.sessionService.setSession({
      event: {
        callId: call.id,
        customerId: call.customerId ?? null,
        phoneNumber: call.phoneNumber,
        direction: 'Inbound',
        status: 'Connected',
        occurredAtUtc: new Date().toISOString()
      },
      customer: call.customerId ? {
        id: call.customerId,
        fullName: call.customerName || 'Customer',
        phone: call.phoneNumber,
        email: null,
        address: null
      } : null
    });

    void this.voiceService.acceptCall();
    this.telephony.acceptCall(call.id).subscribe({
      next: () => {
        void this.router.navigate(['/active-call']);
      },
      error: () => {
        void this.router.navigate(['/active-call']);
      }
    });
  }

  rejectIncomingCallFromDashboard(): void {
    const call = this.dashboard?.currentCall;
    if (!call) return;

    this.voiceService.rejectCall();
    this.telephony.rejectCall(call.id).subscribe({
      next: () => {
        this.loadDashboard();
      },
      error: () => {
        this.loadDashboard();
      }
    });
  }

  navigateToCallHistory(): void {
    void this.router.navigate(['/call-history']);
  }

  viewCallDetails(callId: string): void {
    void this.router.navigate(['/call-history', callId]);
  }

  formatDuration(seconds: number | null | undefined): string {
    if (!seconds || seconds <= 0) return '0s';
    const s = Math.round(seconds);
    const mins = Math.floor(s / 60);
    const rem = s % 60;
    if (mins === 0) return `${rem}s`;
    return `${mins}m ${rem}s`;
  }

  formatWaitTime(enqueuedAtStr: string): string {
    const enqueued = new Date(enqueuedAtStr).getTime();
    const now = Date.now();
    const diffSec = Math.max(0, Math.floor((now - enqueued) / 1000));
    if (diffSec < 60) return `${diffSec}s`;
    const mins = Math.floor(diffSec / 60);
    return `${mins}m`;
  }

  statusClass(status: AgentStatus): string {
    switch (status) {
      case AgentStatus.Available: return 'available';
      case AgentStatus.Busy: return 'busy';
      case AgentStatus.Away: return 'away';
      case AgentStatus.WrapUp: return 'wrapup';
      default: return 'offline';
    }
  }

  statusLabel(status: AgentStatus): string {
    switch (status) {
      case AgentStatus.Available: return 'Available';
      case AgentStatus.Busy: return 'Busy';
      case AgentStatus.Away: return 'Away';
      case AgentStatus.WrapUp: return 'WrapUp';
      default: return 'Offline';
    }
  }

  statusDescription(status: AgentStatus): string {
    switch (status) {
      case AgentStatus.Available: return 'Ready to receive inbound calls from queue.';
      case AgentStatus.Busy: return 'Currently engaged in an active call.';
      case AgentStatus.Away: return 'Stepped away on short break or meal.';
      case AgentStatus.WrapUp: return 'Post-call wrap-up and documentation.';
      default: return 'Offline — Not receiving call routing.';
    }
  }

  callDirectionLabel(direction: number): string {
    return direction === CallDirection.Inbound ? 'Inbound' : 'Outbound';
  }

  callStatusLabel(status: number): string {
    return CallStatus[status] ?? 'Unknown';
  }

  initials(name: string): string {
    return name
      .split(' ')
      .filter(Boolean)
      .slice(0, 2)
      .map(part => part[0])
      .join('')
      .toUpperCase();
  }
}
