import { ChangeDetectionStrategy, ChangeDetectorRef, Component, inject, OnInit } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { CommonModule } from '@angular/common';
import { finalize, forkJoin } from 'rxjs';
import { CallHistoryService } from '../../core/services/call-history.service';
import { CallHistoryItem, CallTimelineEvent } from '../../core/models/call-history.models';
import { CallDirection, CallStatus } from '../../core/models/agent-dashboard.models';

@Component({
  selector: 'app-call-details',
  standalone: true,
  imports: [CommonModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page-header">
      <div>
        <span class="eyebrow">Audit & Inspection</span>
        <h2>Call Session Details</h2>
        <p>Complete 360° call record and chronological lifecycle timeline powered by backend CallEvents.</p>
      </div>
      <div class="header-actions">
        <a routerLink="/call-history" class="button secondary">
          ← Back to Call History
        </a>
        <button class="button primary" type="button" [disabled]="loading" (click)="load()">
          Refresh
        </button>
      </div>
    </div>

    @if (loading) {
      <div class="state-card">
        <div class="spinner"></div>
        <strong>Loading call details and lifecycle timeline...</strong>
        <span>Querying database records.</span>
      </div>
    } @else if (error) {
      <div class="state-card error">
        <strong>Unable to load call record.</strong>
        <span>The requested call could not be retrieved from the server.</span>
        <button class="button secondary" (click)="load()">Retry</button>
      </div>
    } @else if (call) {
      <!-- Overview Banner -->
      <section class="overview-banner">
        <div class="banner-main">
          <div class="banner-title">
            <span class="dir-badge" [class.inbound]="call.direction === CallDirection.Inbound" [class.outbound]="call.direction === CallDirection.Outbound">
              {{call.direction === CallDirection.Inbound ? '📥 Inbound Call' : '📤 Outbound Call'}}
            </span>
            <h3>{{call.customerName || 'Customer'}} ({{call.customerPhone || call.phoneNumber}})</h3>
          </div>
          <div class="banner-tags">
            <span class="status-pill status-{{statusKey(call.status)}}">
              {{statusLabel(call.status)}}
            </span>
            @if (call.dispositionName) {
              <span class="outcome-pill">
                🏷️ {{call.dispositionName}}
              </span>
            }
          </div>
        </div>
        <div class="banner-meta">
          <div class="meta-item">
            <span>Duration</span>
            <strong>{{formatDuration(call.durationSeconds)}}</strong>
          </div>
          <div class="meta-item">
            <span>Started</span>
            <strong>{{call.startedAt | date:'MMM d, h:mm:ss a'}}</strong>
          </div>
          <div class="meta-item">
            <span>Ended</span>
            <strong>{{call.endedAt ? (call.endedAt | date:'MMM d, h:mm:ss a') : 'In progress / active'}}</strong>
          </div>
        </div>
      </section>

      <!-- Details Grid -->
      <div class="detail-grid">
        <!-- Customer Card -->
        <section class="card">
          <div class="card-header">
            <h4>👤 Customer Information</h4>
          </div>
          <div class="detail-rows">
            <div class="detail-row">
              <span>Customer Name</span>
              <strong>{{call.customerName || 'Unknown / Walk-in'}}</strong>
            </div>
            <div class="detail-row">
              <span>Phone Number</span>
              <strong>{{call.customerPhone || call.phoneNumber}}</strong>
            </div>
            <div class="detail-row">
              <span>Customer ID</span>
              <span class="mono-code">{{call.customerId}}</span>
            </div>
          </div>
        </section>

        <!-- Agent Card -->
        <section class="card">
          <div class="card-header">
            <h4>🎧 Agent & Handling</h4>
          </div>
          <div class="detail-rows">
            <div class="detail-row">
              <span>Assigned Agent</span>
              <strong>{{call.agentName || 'Unassigned / System'}}</strong>
            </div>
            <div class="detail-row">
              <span>Agent ID</span>
              <span class="mono-code">{{call.assignedAgentId || '—'}}</span>
            </div>
            <div class="detail-row">
              <span>Direction</span>
              <strong>{{directionLabel(call.direction)}}</strong>
            </div>
            <div class="detail-row">
              <span>Call ID</span>
              <span class="mono-code">{{call.id}}</span>
            </div>
          </div>
        </section>

        <!-- Outcome & Notes Card -->
        <section class="card">
          <div class="card-header">
            <h4>📋 Outcome & Notes</h4>
          </div>
          <div class="detail-rows">
            <div class="detail-row">
              <span>Disposition</span>
              @if (call.dispositionName) {
                <span class="badge-success">🏷️ {{call.dispositionName}}</span>
              } @else {
                <span class="text-muted">Not disposed</span>
              }
            </div>
            <div class="detail-row">
              <span>Follow-up Scheduled</span>
              @if (call.followUpAt) {
                <strong class="text-highlight">📅 {{call.followUpAt | date:'medium'}}</strong>
              } @else {
                <span class="text-muted">None</span>
              }
            </div>
            @if (call.followUpNotes) {
              <div class="detail-block">
                <span>Follow-up Instructions</span>
                <p>{{call.followUpNotes}}</p>
              </div>
            }
            <div class="detail-block">
              <span>Call Notes</span>
              <p>{{call.notes || 'No call wrap-up notes recorded.'}}</p>
            </div>
          </div>
        </section>
      </div>

      <!-- Chronological Call Timeline (Source of Truth) -->
      <section class="timeline-card">
        <div class="card-header timeline-header">
          <div>
            <h3>⏱️ Call Events Lifecycle Timeline</h3>
            <p>Auditable single source of truth captured in real-time from CallEvents.</p>
          </div>
          <span class="event-count-badge">{{timelineEvents.length}} Events Recorded</span>
        </div>

        @if (!timelineEvents.length) {
          <div class="empty-timeline">
            <p>No lifecycle events logged for this call yet.</p>
          </div>
        } @else {
          <div class="timeline-list">
            @for (event of timelineEvents; track event.id; let isLast = $last) {
              <div class="timeline-item" [class.is-last]="isLast">
                <div class="timeline-marker-col">
                  <div class="timeline-icon-box" [ngClass]="eventIconClass(event.eventType)">
                    {{eventIcon(event.eventType)}}
                  </div>
                  @if (!isLast) {
                    <div class="timeline-line"></div>
                  }
                </div>
                <div class="timeline-content">
                  <div class="timeline-event-header">
                    <span class="event-type-badge">{{event.eventType}}</span>
                    <span class="event-time">{{event.occurredAt | date:'MMM d, y, h:mm:ss a'}}</span>
                  </div>
                  <strong class="event-description">{{event.description}}</strong>
                  @if (event.agentName) {
                    <div class="event-agent">
                      <span>Operator:</span> 👤 {{event.agentName}}
                    </div>
                  }
                  @if (getMetadataDetails(event.metadataJson)) {
                    <div class="event-metadata">
                      <code>{{getMetadataDetails(event.metadataJson)}}</code>
                    </div>
                  }
                </div>
              </div>
            }
          </div>
        }
      </section>
    }
  `,
  styles: [`
    :host {
      display: block;
    }

    .page-header {
      display: flex;
      justify-content: space-between;
      align-items: flex-end;
      margin-bottom: 24px;
      gap: 16px;
    }

    .eyebrow {
      font-size: 12px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.08em;
      color: #6366f1;
      display: block;
      margin-bottom: 4px;
    }

    .page-header h2 {
      margin: 0 0 6px 0;
      font-size: 24px;
      font-weight: 800;
      color: #0f172a;
    }

    .page-header p {
      margin: 0;
      font-size: 14px;
      color: #64748b;
    }

    .header-actions {
      display: flex;
      gap: 10px;
      align-items: center;
    }

    .button {
      height: 40px;
      border: 0;
      border-radius: 10px;
      padding: 0 16px;
      font-size: 13px;
      font-weight: 700;
      cursor: pointer;
      display: inline-flex;
      align-items: center;
      gap: 6px;
      text-decoration: none;
      transition: all 0.15s ease;
    }

    .button.primary {
      background: #4f46e5;
      color: #ffffff;
    }

    .button.primary:hover:not(:disabled) {
      background: #4338ca;
    }

    .button.secondary {
      background: #e2e8f0;
      color: #334155;
    }

    .button.secondary:hover:not(:disabled) {
      background: #cbd5e1;
    }

    /* Overview Banner */
    .overview-banner {
      background: #ffffff;
      border: 1px solid #e2e8f0;
      border-radius: 16px;
      padding: 24px;
      margin-bottom: 24px;
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 20px;
      box-shadow: 0 1px 3px rgba(0, 0, 0, 0.04);
    }

    .banner-title {
      display: flex;
      align-items: center;
      gap: 12px;
      margin-bottom: 10px;
    }

    .banner-title h3 {
      margin: 0;
      font-size: 20px;
      font-weight: 800;
      color: #0f172a;
    }

    .dir-badge {
      font-size: 12px;
      font-weight: 700;
      padding: 4px 10px;
      border-radius: 999px;
    }

    .dir-badge.inbound {
      background: #e0f2fe;
      color: #0369a1;
    }

    .dir-badge.outbound {
      background: #f3e8ff;
      color: #7e22ce;
    }

    .banner-tags {
      display: flex;
      gap: 8px;
      align-items: center;
    }

    .status-pill {
      font-size: 12px;
      font-weight: 700;
      padding: 4px 12px;
      border-radius: 999px;
      background: #f1f5f9;
      color: #475569;
    }

    .status-completed { background: #dcfce7; color: #15803d; }
    .status-connected { background: #e0e7ff; color: #4338ca; }
    .status-onhold { background: #fef3c7; color: #b45309; }
    .status-ringing { background: #ffedd5; color: #c2410c; }
    .status-queued { background: #f1f5f9; color: #64748b; }
    .status-rejected { background: #fee2e2; color: #b91c1c; }

    .outcome-pill {
      font-size: 12px;
      font-weight: 700;
      padding: 4px 12px;
      border-radius: 999px;
      background: #ecfdf5;
      color: #047857;
      border: 1px solid #a7f3d0;
    }

    .banner-meta {
      display: flex;
      gap: 28px;
      border-left: 1px solid #e2e8f0;
      padding-left: 28px;
    }

    .meta-item {
      display: grid;
      gap: 4px;
    }

    .meta-item span {
      font-size: 11px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.06em;
      color: #64748b;
    }

    .meta-item strong {
      font-size: 15px;
      color: #0f172a;
    }

    /* Detail Grid */
    .detail-grid {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 20px;
      margin-bottom: 24px;
    }

    .card {
      background: #ffffff;
      border: 1px solid #e2e8f0;
      border-radius: 16px;
      padding: 22px;
      box-shadow: 0 1px 3px rgba(0, 0, 0, 0.04);
    }

    .card-header {
      margin-bottom: 16px;
      padding-bottom: 12px;
      border-bottom: 1px solid #f1f5f9;
    }

    .card-header h4 {
      margin: 0;
      font-size: 15px;
      font-weight: 800;
      color: #0f172a;
    }

    .detail-rows {
      display: grid;
      gap: 12px;
    }

    .detail-row {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 16px;
      font-size: 13px;
    }

    .detail-row span {
      color: #64748b;
      font-weight: 600;
    }

    .detail-row strong {
      color: #0f172a;
      text-align: right;
    }

    .detail-block {
      display: grid;
      gap: 6px;
      font-size: 13px;
      margin-top: 6px;
    }

    .detail-block span {
      color: #64748b;
      font-weight: 700;
      font-size: 11px;
      text-transform: uppercase;
      letter-spacing: 0.06em;
    }

    .detail-block p {
      margin: 0;
      padding: 10px 12px;
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      border-radius: 8px;
      color: #334155;
      font-size: 13px;
      line-height: 1.4;
    }

    .mono-code {
      font-family: monospace;
      font-size: 11px;
      background: #f1f5f9;
      padding: 2px 6px;
      border-radius: 4px;
      color: #475569;
      max-width: 180px;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .badge-success {
      font-weight: 700;
      color: #047857;
      background: #ecfdf5;
      padding: 2px 8px;
      border-radius: 6px;
    }

    .text-highlight {
      color: #4f46e5;
      font-weight: 700;
    }

    .text-muted {
      color: #94a3b8;
    }

    /* Timeline Section */
    .timeline-card {
      background: #ffffff;
      border: 1px solid #e2e8f0;
      border-radius: 16px;
      padding: 24px;
      box-shadow: 0 1px 3px rgba(0, 0, 0, 0.04);
    }

    .timeline-header {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      margin-bottom: 24px;
      padding-bottom: 16px;
      border-bottom: 1px solid #e2e8f0;
    }

    .timeline-header h3 {
      margin: 0 0 4px 0;
      font-size: 18px;
      font-weight: 800;
      color: #0f172a;
    }

    .timeline-header p {
      margin: 0;
      font-size: 13px;
      color: #64748b;
    }

    .event-count-badge {
      font-size: 12px;
      font-weight: 700;
      background: #f1f5f9;
      color: #475569;
      padding: 4px 10px;
      border-radius: 999px;
    }

    .empty-timeline {
      padding: 36px 0;
      text-align: center;
      color: #64748b;
      font-size: 14px;
    }

    .timeline-list {
      display: grid;
      gap: 0;
      padding-left: 10px;
    }

    .timeline-item {
      display: flex;
      gap: 18px;
      position: relative;
    }

    .timeline-marker-col {
      display: flex;
      flex-direction: column;
      align-items: center;
      width: 38px;
    }

    .timeline-icon-box {
      width: 38px;
      height: 38px;
      border-radius: 50%;
      background: #f1f5f9;
      display: grid;
      place-content: center;
      font-size: 16px;
      border: 2px solid #ffffff;
      box-shadow: 0 0 0 2px #e2e8f0;
      z-index: 2;
    }

    .icon-incoming { background: #e0f2fe; box-shadow: 0 0 0 2px #38bdf8; }
    .icon-outgoing { background: #f3e8ff; box-shadow: 0 0 0 2px #c084fc; }
    .icon-queued { background: #fef3c7; box-shadow: 0 0 0 2px #fcd34d; }
    .icon-assigned { background: #e0e7ff; box-shadow: 0 0 0 2px #818cf8; }
    .icon-ringing { background: #ffedd5; box-shadow: 0 0 0 2px #fb923c; }
    .icon-connected { background: #dcfce7; box-shadow: 0 0 0 2px #4ade80; }
    .icon-hold { background: #fef3c7; box-shadow: 0 0 0 2px #f59e0b; }
    .icon-resumed { background: #d1fae5; box-shadow: 0 0 0 2px #10b981; }
    .icon-transferred { background: #ede9fe; box-shadow: 0 0 0 2px #a78bfa; }
    .icon-completed { background: #ecfdf5; box-shadow: 0 0 0 2px #059669; }

    .timeline-line {
      width: 2px;
      flex-grow: 1;
      background: #e2e8f0;
      margin: 4px 0;
      min-height: 28px;
    }

    .timeline-content {
      flex-grow: 1;
      padding-bottom: 24px;
      display: grid;
      gap: 4px;
    }

    .timeline-event-header {
      display: flex;
      align-items: center;
      gap: 10px;
    }

    .event-type-badge {
      font-size: 11px;
      font-weight: 800;
      text-transform: uppercase;
      letter-spacing: 0.06em;
      color: #6366f1;
      background: #eef2ff;
      padding: 2px 7px;
      border-radius: 4px;
    }

    .event-time {
      font-size: 12px;
      color: #64748b;
    }

    .event-description {
      font-size: 14px;
      color: #0f172a;
      font-weight: 700;
    }

    .event-agent {
      font-size: 12px;
      color: #475569;
    }

    .event-agent span {
      color: #94a3b8;
    }

    .event-metadata {
      margin-top: 4px;
    }

    .event-metadata code {
      font-size: 11px;
      font-family: monospace;
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      border-radius: 6px;
      padding: 3px 8px;
      color: #475569;
      display: inline-block;
    }

    /* States */
    .state-card {
      padding: 60px 20px;
      background: #ffffff;
      border: 1px solid #e2e8f0;
      border-radius: 16px;
      display: grid;
      place-content: center;
      justify-items: center;
      gap: 12px;
      text-align: center;
    }

    .state-card strong {
      font-size: 16px;
      color: #0f172a;
    }

    .state-card span {
      font-size: 13px;
      color: #64748b;
    }

    .spinner {
      width: 28px;
      height: 28px;
      border: 3px solid #e2e8f0;
      border-top-color: #4f46e5;
      border-radius: 50%;
      animation: spin 0.8s linear infinite;
    }

    @keyframes spin {
      to { transform: rotate(360deg); }
    }

    @media (max-width: 950px) {
      .overview-banner {
        flex-direction: column;
        align-items: flex-start;
      }
      .banner-meta {
        border-left: 0;
        padding-left: 0;
        border-top: 1px solid #e2e8f0;
        padding-top: 16px;
        width: 100%;
      }
      .detail-grid {
        grid-template-columns: 1fr;
      }
      .page-header {
        flex-direction: column;
        align-items: flex-start;
      }
    }
  `]
})
export class CallDetailsComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly service = inject(CallHistoryService);
  private readonly cdr = inject(ChangeDetectorRef);

  call: CallHistoryItem | null = null;
  timelineEvents: CallTimelineEvent[] = [];
  loading = true;
  error = false;

  readonly CallStatus = CallStatus;
  readonly CallDirection = CallDirection;

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      this.error = true;
      this.loading = false;
      this.cdr.markForCheck();
      return;
    }

    this.loading = true;
    this.error = false;

    forkJoin({
      call: this.service.getById(id),
      timeline: this.service.getTimeline(id)
    }).pipe(
      finalize(() => {
        this.loading = false;
        this.cdr.markForCheck();
      })
    ).subscribe({
      next: ({ call, timeline }) => {
        this.call = call;
        this.timelineEvents = timeline;
      },
      error: () => {
        this.error = true;
      }
    });
  }

  directionLabel(v: CallDirection): string {
    return v === CallDirection.Inbound ? 'Incoming' : 'Outgoing';
  }

  statusLabel(v: CallStatus): string {
    return CallStatus[v] ?? String(v);
  }

  statusKey(v: CallStatus): string {
    const s = CallStatus[v];
    return s ? s.toLowerCase() : 'unknown';
  }

  formatDuration(s?: number | null): string {
    if (s == null) return '—';
    const h = Math.floor(s / 3600);
    const m = Math.floor((s % 3600) / 60);
    const sec = s % 60;
    return h > 0
      ? `${h}h ${String(m).padStart(2, '0')}m ${String(sec).padStart(2, '0')}s`
      : `${String(m).padStart(2, '0')}:${String(sec).padStart(2, '0')}`;
  }

  eventIcon(type: string): string {
    switch (type) {
      case 'Incoming':
      case 'IncomingCallCreated':
        return '📥';
      case 'Outgoing':
      case 'OutgoingCallCreated':
        return '📤';
      case 'Queued':
        return '⏳';
      case 'Assigned':
        return '👤';
      case 'Ringing':
        return '🔔';
      case 'Connected':
        return '📞';
      case 'Hold':
        return '⏸️';
      case 'Resume':
      case 'Resumed':
        return '▶️';
      case 'Transferred':
      case 'TransferredToQueue':
        return '🔀';
      case 'Completed':
        return '✅';
      case 'NotesUpdated':
        return '📝';
      default:
        return 'ℹ️';
    }
  }

  eventIconClass(type: string): string {
    switch (type) {
      case 'Incoming':
      case 'IncomingCallCreated':
        return 'icon-incoming';
      case 'Outgoing':
      case 'OutgoingCallCreated':
        return 'icon-outgoing';
      case 'Queued':
        return 'icon-queued';
      case 'Assigned':
        return 'icon-assigned';
      case 'Ringing':
        return 'icon-ringing';
      case 'Connected':
        return 'icon-connected';
      case 'Hold':
        return 'icon-hold';
      case 'Resume':
      case 'Resumed':
        return 'icon-resumed';
      case 'Transferred':
      case 'TransferredToQueue':
        return 'icon-transferred';
      case 'Completed':
        return 'icon-completed';
      default:
        return '';
    }
  }

  getMetadataDetails(metadataJson?: string | null): string | null {
    if (!metadataJson) return null;
    try {
      const obj = JSON.parse(metadataJson);
      const parts: string[] = [];
      if (obj.disposition) parts.push(`Code: ${obj.disposition}`);
      if (obj.name) parts.push(`Outcome: ${obj.name}`);
      if (obj.holdDurationSeconds != null) parts.push(`Held: ${obj.holdDurationSeconds}s`);
      if (obj.transferType) parts.push(`Transfer: ${obj.transferType}`);
      if (obj.reason) parts.push(`Reason: ${obj.reason}`);
      if (obj.strategy) parts.push(`Strategy: ${obj.strategy}`);
      if (obj.queueName) parts.push(`Queue: ${obj.queueName}`);
      return parts.length > 0 ? parts.join(' | ') : null;
    } catch {
      return null;
    }
  }
}
