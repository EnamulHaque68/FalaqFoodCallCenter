import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CallTrendPoint } from '../../../core/models/call-history.models';

@Component({
  selector: 'app-call-trend-chart',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="trend-card">
      <div class="trend-header">
        <div>
          <span class="eyebrow">Analytics & Volume</span>
          <h3 class="trend-title">7-Day Call Volume Trend</h3>
        </div>
        <div class="trend-legend">
          <span class="legend-item"><span class="dot completed"></span>Completed</span>
          <span class="legend-item"><span class="dot missed"></span>Missed</span>
          <span class="legend-item"><span class="dot rejected"></span>Rejected</span>
        </div>
      </div>

      @if (!trends || trends.length === 0) {
        <div class="empty-chart">No trend data available for this period.</div>
      } @else {
        <div class="chart-container">
          <div class="bars-wrapper">
            @for (point of trends; track point.date) {
              <div class="bar-column" (mouseenter)="activePoint = point" (mouseleave)="activePoint = null">
                <div class="bar-track">
                  <div class="bar-stack">
                    <div class="bar-segment rejected" [style.height.%]="getPercent(point.rejectedCalls)" title="Rejected: {{point.rejectedCalls}}"></div>
                    <div class="bar-segment missed" [style.height.%]="getPercent(point.missedCalls)" title="Missed: {{point.missedCalls}}"></div>
                    <div class="bar-segment completed" [style.height.%]="getPercent(point.completedCalls)" title="Completed: {{point.completedCalls}}"></div>
                  </div>
                </div>
                <div class="bar-label">
                  <span class="day-text">{{ formatDay(point.periodLabel) }}</span>
                  <span class="val-text">{{ point.totalCalls }}</span>
                </div>
              </div>
            }
          </div>
        </div>

        @if (activePoint) {
          <div class="point-detail-banner">
            <strong>{{ activePoint.periodLabel }}</strong>:
            <span>{{ activePoint.totalCalls }} Total</span> ·
            <span class="text-success">{{ activePoint.completedCalls }} Completed</span> ·
            <span class="text-danger">{{ activePoint.missedCalls }} Missed</span> ·
            <span class="text-warning">{{ activePoint.rejectedCalls }} Rejected</span>
            <span class="direction-split">(Inbound: {{ activePoint.incomingCalls }} | Outbound: {{ activePoint.outgoingCalls }})</span>
          </div>
        } @else {
          <div class="point-detail-placeholder">Hover over any day column to view detailed metrics breakdown.</div>
        }
      }
    </div>
  `,
  styles: [`
    .trend-card {
      background: #ffffff;
      border: 1px solid #e2e8f0;
      border-radius: 16px;
      padding: 22px;
      box-shadow: 0 1px 3px rgba(15, 23, 42, 0.04);
      display: flex;
      flex-direction: column;
      gap: 18px;
    }
    .trend-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      flex-wrap: wrap;
      gap: 10px;
    }
    .eyebrow {
      font-size: 11px;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.05em;
      color: #94a3b8;
    }
    .trend-title {
      margin: 2px 0 0;
      font-size: 18px;
      font-weight: 700;
      color: #0f172a;
    }
    .trend-legend {
      display: flex;
      gap: 14px;
      font-size: 12px;
      color: #64748b;
    }
    .legend-item {
      display: flex;
      align-items: center;
      gap: 6px;
    }
    .dot {
      width: 10px;
      height: 10px;
      border-radius: 50%;
    }
    .dot.completed { background: #10b981; }
    .dot.missed { background: #ef4444; }
    .dot.rejected { background: #f59e0b; }

    .chart-container {
      height: 180px;
      display: flex;
      flex-direction: column;
      justify-content: flex-end;
      padding-top: 10px;
    }
    .bars-wrapper {
      display: flex;
      align-items: flex-end;
      justify-content: space-between;
      height: 100%;
      gap: 12px;
    }
    .bar-column {
      flex: 1;
      height: 100%;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: flex-end;
      cursor: pointer;
      transition: transform 0.15s ease;
    }
    .bar-column:hover {
      transform: translateY(-3px);
    }
    .bar-track {
      width: 100%;
      max-width: 44px;
      height: 130px;
      background: #f8fafc;
      border-radius: 8px;
      display: flex;
      align-items: flex-end;
      overflow: hidden;
      border: 1px solid #f1f5f9;
    }
    .bar-stack {
      width: 100%;
      display: flex;
      flex-direction: column-reverse;
      align-items: stretch;
      max-height: 100%;
    }
    .bar-segment {
      width: 100%;
      min-height: 2px;
      transition: height 0.3s ease;
    }
    .bar-segment.completed { background: #10b981; }
    .bar-segment.missed { background: #ef4444; }
    .bar-segment.rejected { background: #f59e0b; }

    .bar-label {
      margin-top: 8px;
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 2px;
    }
    .day-text {
      font-size: 11px;
      color: #64748b;
      font-weight: 500;
    }
    .val-text {
      font-size: 11px;
      color: #0f172a;
      font-weight: 700;
    }

    .point-detail-banner {
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      border-radius: 10px;
      padding: 8px 14px;
      font-size: 12.5px;
      color: #334155;
      display: flex;
      align-items: center;
      gap: 8px;
      flex-wrap: wrap;
    }
    .point-detail-placeholder {
      font-size: 12px;
      color: #94a3b8;
      font-style: italic;
      text-align: center;
      padding: 4px;
    }
    .text-success { color: #16a34a; font-weight: 600; }
    .text-danger { color: #dc2626; font-weight: 600; }
    .text-warning { color: #d97706; font-weight: 600; }
    .direction-split { color: #64748b; font-size: 11.5px; }
    .empty-chart {
      padding: 40px;
      text-align: center;
      color: #94a3b8;
      font-size: 13px;
    }
  `]
})
export class CallTrendChartComponent {
  @Input({ required: true }) trends: CallTrendPoint[] = [];
  activePoint: CallTrendPoint | null = null;

  get maxTotal(): number {
    if (!this.trends || this.trends.length === 0) return 1;
    const max = Math.max(...this.trends.map(t => t.totalCalls));
    return max > 0 ? max : 1;
  }

  getPercent(value: number): number {
    if (!value || value <= 0) return 0;
    return Math.min(100, Math.round((value / this.maxTotal) * 100));
  }

  formatDay(periodLabel: string): string {
    if (!periodLabel) return '';
    // Format "Mon, Sep 08" -> "Mon 08"
    const parts = periodLabel.split(',');
    if (parts.length >= 2) {
      const day = parts[0].trim();
      const monthDay = parts[1].trim().split(' ');
      return `${day} ${monthDay[monthDay.length - 1] || ''}`;
    }
    return periodLabel;
  }
}
