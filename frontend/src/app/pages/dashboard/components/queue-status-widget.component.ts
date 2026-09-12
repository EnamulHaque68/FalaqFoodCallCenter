import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { QueueStatusSummary } from '../../../core/models/call-history.models';

@Component({
  selector: 'app-queue-status-widget',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="queue-card">
      <div class="queue-header">
        <div>
          <span class="eyebrow">Real-Time Routing</span>
          <h3 class="queue-title">Inbound Queue Health</h3>
        </div>
        <div class="queue-badge" [class.has-waiting]="queueStatus.totalWaiting > 0">
          <span class="pulse-queue"></span>
          {{ queueStatus.totalWaiting }} in Queue
        </div>
      </div>

      <div class="queue-stats-row">
        <div class="stat-box">
          <span class="stat-label">Total Waiting</span>
          <strong class="stat-val">{{ queueStatus.totalWaiting }}</strong>
        </div>
        <div class="stat-box">
          <span class="stat-label">Average Wait</span>
          <strong class="stat-val">{{ formatSeconds(queueStatus.averageWaitSeconds) }}</strong>
        </div>
        <div class="stat-box" [class.warning]="queueStatus.longestWaitSeconds > 60">
          <span class="stat-label">Longest Wait</span>
          <strong class="stat-val">{{ formatSeconds(queueStatus.longestWaitSeconds) }}</strong>
        </div>
      </div>

      @if (!queueStatus.entries || queueStatus.entries.length === 0) {
        <div class="queue-clear-state">
          <div class="clear-icon">✓</div>
          <div class="clear-title">Queue is clear</div>
          <div class="clear-subtitle">No inbound callers are waiting in queue.</div>
        </div>
      } @else {
        <div class="queue-list">
          @for (entry of queueStatus!.entries; track entry.callId) {
            <div class="queue-item">
              <div class="pos-badge">#{{ entry.position }}</div>
              <div class="queue-details">
                <div class="phone-row">
                  <span class="phone-number">{{ entry.phoneNumber }}</span>
                  <span class="queue-tag">{{ entry.queueName || 'General' }}</span>
                </div>
                <div class="time-meta">Waiting: {{ formatWaitTime(entry.enqueuedAt) }}</div>
              </div>
            </div>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .queue-card {
      background: #ffffff;
      border: 1px solid #e2e8f0;
      border-radius: 16px;
      padding: 22px;
      box-shadow: 0 1px 3px rgba(15, 23, 42, 0.04);
      display: flex;
      flex-direction: column;
      gap: 16px;
    }
    .queue-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
    }
    .eyebrow {
      font-size: 11px;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.05em;
      color: #94a3b8;
    }
    .queue-title {
      margin: 2px 0 0;
      font-size: 18px;
      font-weight: 700;
      color: #0f172a;
    }
    .queue-badge {
      display: flex;
      align-items: center;
      gap: 6px;
      font-size: 12px;
      font-weight: 600;
      padding: 4px 10px;
      border-radius: 9999px;
      background: #f1f5f9;
      color: #64748b;
    }
    .queue-badge.has-waiting {
      background: #fef2f2;
      color: #dc2626;
    }
    .pulse-queue {
      width: 7px;
      height: 7px;
      border-radius: 50%;
      background: currentColor;
    }

    .queue-stats-row {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 10px;
      background: #f8fafc;
      border-radius: 12px;
      padding: 12px;
    }
    .stat-box {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }
    .stat-label {
      font-size: 11px;
      color: #64748b;
      font-weight: 500;
    }
    .stat-val {
      font-size: 16px;
      font-weight: 700;
      color: #0f172a;
    }
    .stat-box.warning .stat-val {
      color: #dc2626;
    }

    .queue-clear-state {
      padding: 30px 20px;
      text-align: center;
      background: #f8fafc;
      border-radius: 12px;
      border: 1px dashed #e2e8f0;
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 4px;
    }
    .clear-icon {
      width: 32px;
      height: 32px;
      border-radius: 50%;
      background: #ecfdf5;
      color: #059669;
      font-weight: 700;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 16px;
      margin-bottom: 4px;
    }
    .clear-title {
      font-size: 13.5px;
      font-weight: 600;
      color: #0f172a;
    }
    .clear-subtitle {
      font-size: 12px;
      color: #64748b;
    }

    .queue-list {
      display: flex;
      flex-direction: column;
      gap: 8px;
      max-height: 240px;
      overflow-y: auto;
    }
    .queue-item {
      display: flex;
      align-items: center;
      gap: 12px;
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      border-radius: 10px;
      padding: 10px 14px;
    }
    .pos-badge {
      width: 28px;
      height: 28px;
      border-radius: 50%;
      background: #2563eb;
      color: #ffffff;
      font-size: 11px;
      font-weight: 700;
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
    }
    .queue-details {
      flex: 1;
      min-width: 0;
    }
    .phone-row {
      display: flex;
      align-items: center;
      gap: 8px;
    }
    .phone-number {
      font-size: 13px;
      font-weight: 700;
      color: #0f172a;
    }
    .queue-tag {
      font-size: 10px;
      background: #e2e8f0;
      color: #475569;
      padding: 1px 5px;
      border-radius: 4px;
      font-weight: 600;
    }
    .time-meta {
      font-size: 11px;
      color: #64748b;
      margin-top: 2px;
    }
  `]
})
export class QueueStatusWidgetComponent {
  @Input({ required: true }) queueStatus!: QueueStatusSummary;

  formatSeconds(seconds: number): string {
    if (!seconds || seconds <= 0) return '0s';
    const mins = Math.floor(seconds / 60);
    const secs = Math.round(seconds % 60);
    if (mins === 0) return `${secs}s`;
    return `${mins}m ${secs}s`;
  }

  formatWaitTime(enqueuedAtIso: string): string {
    if (!enqueuedAtIso) return '0s';
    const enq = new Date(enqueuedAtIso).getTime();
    const now = Date.now();
    const diffSec = Math.max(0, Math.floor((now - enq) / 1000));
    return this.formatSeconds(diffSec);
  }
}
