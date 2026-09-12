import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AgentStatusItem, AgentStatusSummary } from '../../../core/models/call-history.models';

@Component({
  selector: 'app-agent-status-grid',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="roster-card">
      <div class="roster-header">
        <div>
          <span class="eyebrow">Workforce Operations</span>
          <h3 class="roster-title">Live Agent Status</h3>
        </div>
        <div class="roster-filters">
          <button
            type="button"
            class="filter-pill"
            [class.active]="selectedStatus === 'all'"
            (click)="selectedStatus = 'all'">
            All <span class="count">{{ summary.total || 0 }}</span>
          </button>
          <button
            type="button"
            class="filter-pill available"
            [class.active]="selectedStatus === 'available'"
            (click)="selectedStatus = 'available'">
            Available <span class="count">{{ summary.available || 0 }}</span>
          </button>
          <button
            type="button"
            class="filter-pill busy"
            [class.active]="selectedStatus === 'busy'"
            (click)="selectedStatus = 'busy'">
            Busy <span class="count">{{ summary.busy || 0 }}</span>
          </button>
          <button
            type="button"
            class="filter-pill away"
            [class.active]="selectedStatus === 'away'"
            (click)="selectedStatus = 'away'">
            Away <span class="count">{{ summary.away || 0 }}</span>
          </button>
          <button
            type="button"
            class="filter-pill offline"
            [class.active]="selectedStatus === 'offline'"
            (click)="selectedStatus = 'offline'">
            Offline <span class="count">{{ summary.offline || 0 }}</span>
          </button>
        </div>
      </div>

      @if (filteredAgents.length === 0) {
        <div class="empty-roster">No agents found for this status filter.</div>
      } @else {
        <div class="agents-grid">
          @for (agent of filteredAgents; track agent.id) {
            <div class="agent-card" [ngClass]="getStatusClass(agent.status)">
              <div class="agent-top">
                <div class="avatar">{{ getInitials(agent.displayName) }}</div>
                <div class="agent-info">
                  <div class="agent-name">{{ agent.displayName }}</div>
                  <div class="agent-meta">
                    <span class="code">{{ agent.employeeCode }}</span>
                    @if (agent.team) {
                      <span class="team-tag">{{ agent.team }}</span>
                    }
                  </div>
                </div>
                <div class="status-indicator">
                  <span class="pulse-dot"></span>
                  <span class="status-text">{{ getStatusLabel(agent.status) }}</span>
                </div>
              </div>

              @if (agent.currentCallPhoneNumber) {
                <div class="active-call-bar">
                  <span class="call-icon">📞</span>
                  <span class="call-phone">{{ agent.currentCallPhoneNumber }}</span>
                  <span class="live-pill">LIVE</span>
                </div>
              }
            </div>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .roster-card {
      background: #ffffff;
      border: 1px solid #e2e8f0;
      border-radius: 16px;
      padding: 22px;
      box-shadow: 0 1px 3px rgba(15, 23, 42, 0.04);
      display: flex;
      flex-direction: column;
      gap: 18px;
    }
    .roster-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      flex-wrap: wrap;
      gap: 12px;
    }
    .eyebrow {
      font-size: 11px;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.05em;
      color: #94a3b8;
    }
    .roster-title {
      margin: 2px 0 0;
      font-size: 18px;
      font-weight: 700;
      color: #0f172a;
    }

    .roster-filters {
      display: flex;
      gap: 8px;
      flex-wrap: wrap;
    }
    .filter-pill {
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      padding: 5px 10px;
      border-radius: 9999px;
      font-size: 12px;
      font-weight: 500;
      color: #64748b;
      cursor: pointer;
      display: flex;
      align-items: center;
      gap: 6px;
      transition: all 0.15s ease;
    }
    .filter-pill .count {
      background: #e2e8f0;
      color: #334155;
      padding: 1px 6px;
      border-radius: 9999px;
      font-size: 11px;
      font-weight: 700;
    }
    .filter-pill:hover {
      background: #f1f5f9;
      color: #0f172a;
    }
    .filter-pill.active {
      background: #0f172a;
      color: #ffffff;
      border-color: #0f172a;
    }
    .filter-pill.active .count {
      background: #334155;
      color: #ffffff;
    }

    .filter-pill.available.active {
      background: #10b981;
      border-color: #10b981;
    }
    .filter-pill.available.active .count { background: #047857; }

    .filter-pill.busy.active {
      background: #f59e0b;
      border-color: #f59e0b;
    }
    .filter-pill.busy.active .count { background: #b45309; }

    .filter-pill.away.active {
      background: #8b5cf6;
      border-color: #8b5cf6;
    }
    .filter-pill.away.active .count { background: #6d28d9; }

    .agents-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
      gap: 12px;
    }
    .agent-card {
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      border-radius: 12px;
      padding: 14px;
      display: flex;
      flex-direction: column;
      gap: 10px;
      transition: all 0.15s ease;
    }
    .agent-card:hover {
      background: #ffffff;
      box-shadow: 0 4px 12px rgba(15, 23, 42, 0.06);
    }
    .agent-top {
      display: flex;
      align-items: center;
      gap: 10px;
    }
    .avatar {
      width: 36px;
      height: 36px;
      border-radius: 50%;
      background: #e2e8f0;
      color: #334155;
      font-weight: 700;
      font-size: 13px;
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
    }
    .agent-info {
      flex: 1;
      min-width: 0;
    }
    .agent-name {
      font-size: 13.5px;
      font-weight: 600;
      color: #0f172a;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    .agent-meta {
      display: flex;
      align-items: center;
      gap: 6px;
      margin-top: 2px;
    }
    .code {
      font-size: 11px;
      color: #64748b;
      font-weight: 500;
    }
    .team-tag {
      font-size: 10px;
      background: #e2e8f0;
      color: #475569;
      padding: 1px 5px;
      border-radius: 4px;
      font-weight: 600;
    }

    .status-indicator {
      display: flex;
      align-items: center;
      gap: 5px;
      font-size: 11px;
      font-weight: 600;
      padding: 3px 8px;
      border-radius: 9999px;
      flex-shrink: 0;
    }
    .pulse-dot {
      width: 7px;
      height: 7px;
      border-radius: 50%;
    }

    /* Status classes */
    .status-available .status-indicator { background: #ecfdf5; color: #059669; }
    .status-available .pulse-dot { background: #10b981; }

    .status-busy .status-indicator { background: #fffbeb; color: #d97706; }
    .status-busy .pulse-dot {
      background: #f59e0b;
      box-shadow: 0 0 0 2px rgba(245, 158, 11, 0.3);
      animation: pulse 1.5s infinite;
    }

    .status-away .status-indicator { background: #f5f3ff; color: #7c3aed; }
    .status-away .pulse-dot { background: #8b5cf6; }

    .status-offline .status-indicator { background: #f1f5f9; color: #94a3b8; }
    .status-offline .pulse-dot { background: #cbd5e1; }

    .active-call-bar {
      background: #fef3c7;
      border: 1px solid #fde68a;
      border-radius: 8px;
      padding: 6px 10px;
      display: flex;
      align-items: center;
      gap: 6px;
      font-size: 12px;
      color: #92400e;
    }
    .call-icon { font-size: 13px; }
    .call-phone { font-weight: 700; flex: 1; }
    .live-pill {
      font-size: 9.5px;
      font-weight: 800;
      background: #d97706;
      color: #ffffff;
      padding: 1px 4px;
      border-radius: 4px;
      letter-spacing: 0.05em;
    }

    @keyframes pulse {
      0% { transform: scale(0.95); opacity: 0.8; }
      50% { transform: scale(1.15); opacity: 1; }
      100% { transform: scale(0.95); opacity: 0.8; }
    }

    .empty-roster {
      padding: 30px;
      text-align: center;
      color: #94a3b8;
      font-size: 13px;
    }
  `]
})
export class AgentStatusGridComponent {
  @Input({ required: true }) summary!: AgentStatusSummary;
  selectedStatus: 'all' | 'available' | 'busy' | 'away' | 'offline' = 'all';

  get filteredAgents(): AgentStatusItem[] {
    if (!this.summary?.agents) return [];
    if (this.selectedStatus === 'all') return this.summary.agents;

    const statusMap: Record<string, number> = {
      offline: 1,
      available: 2,
      busy: 3,
      away: 5
    };
    const targetStatus = statusMap[this.selectedStatus];
    return this.summary.agents.filter(a => a.status === targetStatus);
  }

  getStatusClass(status: number): string {
    switch (status) {
      case 2: return 'status-available';
      case 3: return 'status-busy';
      case 4: return 'status-busy'; // WrapUp
      case 5: return 'status-away';
      default: return 'status-offline';
    }
  }

  getStatusLabel(status: number): string {
    switch (status) {
      case 2: return 'Available';
      case 3: return 'Busy';
      case 4: return 'Wrap Up';
      case 5: return 'Away';
      default: return 'Offline';
    }
  }

  getInitials(name: string): string {
    if (!name) return '??';
    const parts = name.trim().split(' ');
    if (parts.length >= 2) {
      return (parts[0][0] + parts[1][0]).toUpperCase();
    }
    return name.slice(0, 2).toUpperCase();
  }
}
