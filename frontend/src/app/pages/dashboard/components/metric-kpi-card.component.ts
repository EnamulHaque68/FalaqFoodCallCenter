import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-metric-kpi-card',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="kpi-card" [ngClass]="'theme-' + (theme || 'default')">
      <div class="kpi-top">
        <span class="kpi-label">{{ label }}</span>
        @if (icon) {
          <div class="kpi-icon-badge">{{ icon }}</div>
        }
      </div>
      <div class="kpi-value-row">
        <span class="kpi-value">{{ value }}</span>
        @if (changePercent !== undefined && changePercent !== null) {
          <span class="kpi-trend" [class.positive]="changePercent >= 0" [class.negative]="changePercent < 0">
            {{ changePercent >= 0 ? '↑ +' : '↓ ' }}{{ changePercent }}%
          </span>
        }
      </div>
      @if (subtext) {
        <div class="kpi-subtext">{{ subtext }}</div>
      }
    </div>
  `,
  styles: [`
    .kpi-card {
      background: #ffffff;
      border: 1px solid #e2e8f0;
      border-radius: 14px;
      padding: 16px 18px;
      box-shadow: 0 1px 3px rgba(15, 23, 42, 0.04), 0 1px 2px rgba(15, 23, 42, 0.02);
      transition: transform 0.15s ease, box-shadow 0.15s ease;
      position: relative;
      overflow: hidden;
    }
    .kpi-card:hover {
      transform: translateY(-2px);
      box-shadow: 0 6px 16px rgba(15, 23, 42, 0.08);
    }
    .kpi-top {
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-bottom: 8px;
    }
    .kpi-label {
      font-size: 11.5px;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.05em;
      color: #64748b;
    }
    .kpi-icon-badge {
      width: 32px;
      height: 32px;
      border-radius: 8px;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 15px;
      background: #f1f5f9;
      color: #334155;
    }
    .kpi-value-row {
      display: flex;
      align-items: baseline;
      gap: 8px;
    }
    .kpi-value {
      font-size: 26px;
      font-weight: 700;
      color: #0f172a;
      letter-spacing: -0.02em;
      line-height: 1.2;
    }
    .kpi-trend {
      font-size: 11px;
      font-weight: 600;
      padding: 2px 6px;
      border-radius: 9999px;
    }
    .kpi-trend.positive {
      background: #ecfdf5;
      color: #059669;
    }
    .kpi-trend.negative {
      background: #fef2f2;
      color: #dc2626;
    }
    .kpi-subtext {
      margin-top: 6px;
      font-size: 11.5px;
      color: #94a3b8;
    }

    /* Themes */
    .theme-primary .kpi-value { color: #2563eb; }
    .theme-primary .kpi-icon-badge { background: #eff6ff; color: #2563eb; }

    .theme-success .kpi-value { color: #16a34a; }
    .theme-success .kpi-icon-badge { background: #f0fdf4; color: #16a34a; }

    .theme-warning .kpi-value { color: #d97706; }
    .theme-warning .kpi-icon-badge { background: #fffbeb; color: #d97706; }

    .theme-danger .kpi-value { color: #dc2626; }
    .theme-danger .kpi-icon-badge { background: #fef2f2; color: #dc2626; }

    .theme-purple .kpi-value { color: #7c3aed; }
    .theme-purple .kpi-icon-badge { background: #f5f3ff; color: #7c3aed; }

    .theme-cyan .kpi-value { color: #0891b2; }
    .theme-cyan .kpi-icon-badge { background: #ecfeff; color: #0891b2; }
  `]
})
export class MetricKpiCardComponent {
  @Input({ required: true }) label!: string;
  @Input({ required: true }) value!: string | number;
  @Input() subtext?: string;
  @Input() icon?: string;
  @Input() theme: 'default' | 'primary' | 'success' | 'warning' | 'danger' | 'purple' | 'cyan' = 'default';
  @Input() changePercent?: number | null;
}
