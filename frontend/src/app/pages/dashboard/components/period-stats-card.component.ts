import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PeriodStats } from '../../../core/models/call-history.models';

@Component({
  selector: 'app-period-stats-card',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="period-card">
      <div class="period-header">
        <div>
          <span class="period-eyebrow">{{ subtitle || 'Historical Snapshot' }}</span>
          <h3 class="period-title">{{ stats.periodName || title }}</h3>
        </div>
        <span class="period-badge" [ngClass]="'badge-' + (theme || 'blue')">
          {{ stats.totalCalls || 0 }} Calls
        </span>
      </div>

      <div class="completion-section">
        <div class="completion-label-row">
          <span>Resolution Rate</span>
          <strong class="rate-value">{{ stats.completionRatePercent || 0 }}%</strong>
        </div>
        <div class="progress-track">
          <div class="progress-fill" [style.width.%]="stats.completionRatePercent || 0"></div>
        </div>
      </div>

      <div class="submetrics-grid">
        <div class="submetric-item">
          <span class="sub-label">Incoming</span>
          <strong class="sub-val">{{ stats.incoming || 0 }}</strong>
        </div>
        <div class="submetric-item">
          <span class="sub-label">Outgoing</span>
          <strong class="sub-val">{{ stats.outgoing || 0 }}</strong>
        </div>
        <div class="submetric-item success">
          <span class="sub-label">Completed</span>
          <strong class="sub-val">{{ stats.completed || 0 }}</strong>
        </div>
        <div class="submetric-item danger">
          <span class="sub-label">Missed</span>
          <strong class="sub-val">{{ stats.missed || 0 }}</strong>
        </div>
        <div class="submetric-item warning">
          <span class="sub-label">Rejected</span>
          <strong class="sub-val">{{ stats.rejected || 0 }}</strong>
        </div>
        <div class="submetric-item">
          <span class="sub-label">Avg Duration</span>
          <strong class="sub-val">{{ formatDuration(stats.averageDurationSeconds || 0) }}</strong>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .period-card {
      background: #ffffff;
      border: 1px solid #e2e8f0;
      border-radius: 16px;
      padding: 20px;
      box-shadow: 0 1px 3px rgba(15, 23, 42, 0.04);
      display: flex;
      flex-direction: column;
      gap: 16px;
    }
    .period-header {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
    }
    .period-eyebrow {
      font-size: 11px;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.05em;
      color: #94a3b8;
    }
    .period-title {
      margin: 2px 0 0;
      font-size: 18px;
      font-weight: 700;
      color: #0f172a;
    }
    .period-badge {
      font-size: 12px;
      font-weight: 600;
      padding: 4px 10px;
      border-radius: 9999px;
    }
    .badge-blue { background: #eff6ff; color: #2563eb; }
    .badge-purple { background: #f5f3ff; color: #7c3aed; }
    .badge-teal { background: #f0fdfa; color: #0d9488; }

    .completion-section {
      display: flex;
      flex-direction: column;
      gap: 6px;
    }
    .completion-label-row {
      display: flex;
      justify-content: space-between;
      align-items: center;
      font-size: 12px;
      color: #64748b;
    }
    .rate-value {
      font-size: 13px;
      font-weight: 700;
      color: #16a34a;
    }
    .progress-track {
      width: 100%;
      height: 7px;
      background: #f1f5f9;
      border-radius: 9999px;
      overflow: hidden;
    }
    .progress-fill {
      height: 100%;
      background: linear-gradient(90deg, #10b981, #059669);
      border-radius: 9999px;
      transition: width 0.4s ease-out;
    }

    .submetrics-grid {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 10px;
      background: #f8fafc;
      border-radius: 12px;
      padding: 12px;
    }
    .submetric-item {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }
    .sub-label {
      font-size: 11px;
      color: #64748b;
      font-weight: 500;
    }
    .sub-val {
      font-size: 15px;
      font-weight: 700;
      color: #0f172a;
    }
    .submetric-item.success .sub-val { color: #16a34a; }
    .submetric-item.danger .sub-val { color: #dc2626; }
    .submetric-item.warning .sub-val { color: #d97706; }
  `]
})
export class PeriodStatsCardComponent {
  @Input({ required: true }) stats!: PeriodStats;
  @Input() title: string = 'Statistics';
  @Input() subtitle?: string;
  @Input() theme: 'blue' | 'purple' | 'teal' = 'blue';

  formatDuration(seconds: number): string {
    const mins = Math.floor(seconds / 60);
    const secs = Math.round(seconds % 60);
    return `${String(mins).padStart(2, '0')}:${String(secs).padStart(2, '0')}`;
  }
}
