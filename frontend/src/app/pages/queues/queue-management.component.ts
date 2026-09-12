import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  OnDestroy,
  inject,
  signal
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription, interval } from 'rxjs';
import { QueueService } from '../../core/services/queue.service';
import { AgentService } from '../../core/services/agent.service';
import { CallCenterRealtimeService } from '../../core/services/call-center-realtime.service';
import { CallQueue, QueueSummary, CallQueueEntry } from '../../core/models/queue.models';
import { Agent } from '../../core/models/agent.models';
import { AgentStatus } from '../../core/models/agent-dashboard.models';

@Component({
  selector: 'app-queue-management',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="queue-page">
      <!-- Top Header -->
      <div class="header-row">
        <div>
          <span class="eyebrow">Real-Time Routing & Queueing</span>
          <h1>Call Queue Management</h1>
          <p class="subtitle">Live monitoring, dynamic positioning, priority dispatching, and automated call routing.</p>
        </div>
        <div class="header-actions">
          <button class="btn btn-secondary" (click)="triggerAutoAssign()" [disabled]="isAssigning()">
            ⚡ Auto-Assign Next Call
          </button>
          <button class="btn btn-primary" (click)="openCreateModal()">
            + Create New Queue
          </button>
        </div>
      </div>

      <!-- Live KPI Metrics Bar -->
      <div class="metrics-grid">
        <div class="metric-card" [class.highlight]="(summary()?.totalWaitingCalls ?? 0) > 0">
          <div class="metric-label">Waiting In Queue</div>
          <div class="metric-val">
            {{ summary()?.totalWaitingCalls ?? 0 }}
            @if ((summary()?.totalWaitingCalls ?? 0) > 0) {
              <span class="pulsing-dot" title="Active waiting calls"></span>
            }
          </div>
          <div class="metric-hint">Active calls awaiting agent</div>
        </div>

        <div class="metric-card">
          <div class="metric-label">Longest Wait Time</div>
          <div class="metric-val text-amber">
            {{ formatDuration(summary()?.longestWaitSeconds ?? 0) }}
          </div>
          <div class="metric-hint">Current maximum queue latency</div>
        </div>

        <div class="metric-card">
          <div class="metric-label">Average Wait Time</div>
          <div class="metric-val text-cyan">
            {{ formatDuration(summary()?.averageWaitSeconds ?? 0) }}
          </div>
          <div class="metric-hint">Across all active queues</div>
        </div>

        <div class="metric-card">
          <div class="metric-label">Available Agents</div>
          <div class="metric-val text-emerald">
            {{ summary()?.availableAgentsCount ?? 0 }}
          </div>
          <div class="metric-hint">Ready for instant assignment</div>
        </div>

        <div class="metric-card">
          <div class="metric-label">Active Queues</div>
          <div class="metric-val text-purple">
            {{ summary()?.activeQueues ?? 0 }} / {{ summary()?.totalQueues ?? 0 }}
          </div>
          <div class="metric-hint">Configured inbound queues</div>
        </div>
      </div>

      <!-- Alerts / Toast -->
      @if (notice()) {
        <div class="alert-notice" [class.alert-error]="notice()?.isError">
          <span>{{ notice()?.message }}</span>
          <button class="close-btn" (click)="clearNotice()">✕</button>
        </div>
      }

      <!-- Main Layout: Queues Selector & Entries Board -->
      <div class="content-split">
        <!-- Left: Queues Cards List -->
        <div class="queues-panel">
          <div class="panel-header">
            <h3>Queues ({{ queues().length }})</h3>
            <button class="icon-btn" (click)="loadAll()" title="Refresh data">🔄</button>
          </div>

          <div class="queue-cards-list">
            @for (q of queues(); track q.id) {
              <div
                class="queue-card"
                [class.selected]="selectedQueue()?.id === q.id"
                [class.inactive]="!q.isActive"
                (click)="selectQueue(q)">
                <div class="queue-card-top">
                  <div class="queue-name-wrap">
                    <span class="queue-name">{{ q.name }}</span>
                    <span class="badge badge-priority" [title]="'Queue priority ' + q.priority">
                      P{{ q.priority }}
                    </span>
                  </div>
                  <span
                    class="status-pill"
                    [class.pill-active]="q.isActive"
                    [class.pill-inactive]="!q.isActive">
                    {{ q.isActive ? 'Active' : 'Paused' }}
                  </span>
                </div>

                <div class="queue-card-body">
                  <div class="stat-pill">
                    <span class="count-badge" [class.count-high]="q.waitingCallsCount > 0">
                      {{ q.waitingCallsCount }}
                    </span>
                    <span class="stat-text">calls waiting</span>
                  </div>
                  <div class="stat-meta">
                    Avg: {{ formatDuration(q.averageWaitSeconds) }}
                  </div>
                </div>

                <div class="queue-card-actions" (click)="$event.stopPropagation()">
                  <button class="link-btn" (click)="openEditModal(q)">Edit</button>
                  @if (q.isActive) {
                    <button class="link-btn text-danger" (click)="deleteQueue(q)">Deactivate</button>
                  }
                </div>
              </div>
            } @empty {
              <div class="empty-state">No queues found. Click "+ Create New Queue" to start.</div>
            }
          </div>
        </div>

        <!-- Right: Live Queue Entries Workspace -->
        <div class="entries-panel">
          <div class="panel-header">
            <div>
              <h3>
                @if (selectedQueue()) {
                  Entries for {{ selectedQueue()?.name }}
                } @else {
                  Select a queue to view waiting callers
                }
              </h3>
              <p class="panel-desc">Calls are automatically prioritized by Priority and Enqueued timestamp.</p>
            </div>
            @if (selectedQueue()) {
              <div class="panel-actions">
                <span class="badge badge-waiting">
                  {{ entries().length }} Call{{ entries().length === 1 ? '' : 's' }} Waiting
                </span>
              </div>
            }
          </div>

          @if (selectedQueue()) {
            @if (loadingEntries()) {
              <div class="loading-wrap">Loading queue entries...</div>
            } @else if (entries().length === 0) {
              <div class="empty-entries">
                <div class="empty-icon">🎉</div>
                <h4>Queue is Empty</h4>
                <p>There are no waiting callers in this queue right now.</p>
              </div>
            } @else {
              <div class="table-responsive">
                <table class="entries-table">
                  <thead>
                    <tr>
                      <th style="width: 70px;">Pos</th>
                      <th style="width: 90px;">Priority</th>
                      <th>Caller</th>
                      <th>Enqueued At</th>
                      <th>Wait Time</th>
                      <th style="text-align: right;">Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (entry of entries(); track entry.id) {
                      <tr [class.gold-row]="entry.position === 1">
                        <td>
                          <span class="pos-badge" [class.pos-first]="entry.position === 1">
                            #{{ entry.position }}
                          </span>
                        </td>
                        <td>
                          <div class="priority-cell">
                            <span class="badge" [class.badge-vip]="entry.priority > 0">
                              {{ entry.priority > 0 ? 'VIP (' + entry.priority + ')' : 'Normal' }}
                            </span>
                            <button
                              class="mini-btn"
                              title="Increase priority (+1)"
                              (click)="bumpPriority(entry)">
                              ▲
                            </button>
                          </div>
                        </td>
                        <td>
                          <div class="caller-info">
                            <span class="caller-phone">📞 {{ entry.phoneNumber }}</span>
                            <span class="caller-name">{{ entry.customerName || 'Unknown Caller' }}</span>
                          </div>
                        </td>
                        <td>
                          <span class="time-text">{{ entry.enqueuedAt | date:'HH:mm:ss' }}</span>
                        </td>
                        <td>
                          <span class="timer-badge" [class.timer-warn]="getLiveWait(entry) > 120" [class.timer-danger]="getLiveWait(entry) > 300">
                            ⏱️ {{ formatDuration(getLiveWait(entry)) }}
                          </span>
                        </td>
                        <td style="text-align: right;">
                          <div class="row-actions">
                            <button
                              class="btn btn-sm btn-assign"
                              (click)="openAssignModal(entry)">
                              Assign Agent
                            </button>
                            <button
                              class="btn btn-sm btn-cancel"
                              (click)="cancelCall(entry)"
                              title="Cancel / remove call from queue">
                              Cancel
                            </button>
                          </div>
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            }
          } @else {
            <div class="no-selection">
              <span>👈 Select a queue on the left to inspect its live waiting callers.</span>
            </div>
          }
        </div>
      </div>

      <!-- Create / Edit Queue Modal -->
      @if (showQueueModal()) {
        <div class="modal-backdrop" (click)="closeQueueModal()">
          <div class="modal-card" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <h3>{{ editingQueueId() ? 'Edit Call Queue' : 'Create New Call Queue' }}</h3>
              <button class="close-btn" (click)="closeQueueModal()">✕</button>
            </div>
            <div class="modal-body">
              <div class="form-group">
                <label>Queue Name *</label>
                <input
                  type="text"
                  [(ngModel)]="queueForm.name"
                  placeholder="e.g. VIP Support, Bengali Inbound, Sales Dispatch"
                  class="form-input" />
              </div>

              <div class="form-group">
                <label>Queue Priority (1 = standard, 10 = critical)</label>
                <input
                  type="number"
                  [(ngModel)]="queueForm.priority"
                  min="1"
                  max="100"
                  class="form-input" />
              </div>

              <div class="form-group checkbox-group">
                <label class="checkbox-label">
                  <input type="checkbox" [(ngModel)]="queueForm.isActive" />
                  Active (Receiving calls)
                </label>
              </div>
            </div>
            <div class="modal-footer">
              <button class="btn btn-secondary" (click)="closeQueueModal()">Cancel</button>
              <button class="btn btn-primary" (click)="saveQueue()" [disabled]="!queueForm.name.trim()">
                {{ editingQueueId() ? 'Update Queue' : 'Create Queue' }}
              </button>
            </div>
          </div>
        </div>
      }

      <!-- Direct Assign Modal -->
      @if (showAssignModal()) {
        <div class="modal-backdrop" (click)="closeAssignModal()">
          <div class="modal-card" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <h3>Directly Assign Call</h3>
              <button class="close-btn" (click)="closeAssignModal()">✕</button>
            </div>
            <div class="modal-body">
              <p style="color:#94a3b8;margin-bottom:12px;">
                Assign caller <strong>{{ assigningEntry()?.phoneNumber }}</strong> to an active Available agent:
              </p>

              @if (availableAgents().length === 0) {
                <div class="alert-notice alert-error">
                  No agents are currently in "Available" status. You can wait for an agent to become ready or use auto-dispatch.
                </div>
              } @else {
                <div class="agents-pick-list">
                  @for (agent of availableAgents(); track agent.id) {
                    <div
                      class="agent-pick-item"
                      [class.selected]="selectedAgentId() === agent.id"
                      (click)="selectedAgentId.set(agent.id)">
                      <div class="agent-avatar">{{ agent.displayName.charAt(0) }}</div>
                      <div>
                        <strong>{{ agent.displayName }}</strong> ({{ agent.employeeCode }})
                        <div style="font-size:12px;color:#94a3b8;">{{ agent.team || 'General Team' }}</div>
                      </div>
                      <span class="status-dot-green" title="Available"></span>
                    </div>
                  }
                </div>
              }
            </div>
            <div class="modal-footer">
              <button class="btn btn-secondary" (click)="closeAssignModal()">Cancel</button>
              <button
                class="btn btn-primary"
                [disabled]="!selectedAgentId() || isAssigning()"
                (click)="confirmDirectAssign()">
                Assign to Selected Agent
              </button>
            </div>
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    .queue-page {
      display: flex;
      flex-direction: column;
      gap: 20px;
      color: #e2e8f0;
    }

    .header-row {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      flex-wrap: wrap;
      gap: 16px;
    }

    .eyebrow {
      font-size: 12px;
      text-transform: uppercase;
      letter-spacing: 1px;
      color: #38bdf8;
      font-weight: 700;
    }

    h1 {
      font-size: 26px;
      font-weight: 800;
      color: #f8fafc;
      margin: 4px 0;
    }

    .subtitle {
      color: #94a3b8;
      font-size: 14px;
      margin: 0;
    }

    .header-actions {
      display: flex;
      gap: 10px;
    }

    /* Metrics Grid */
    .metrics-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(190px, 1fr));
      gap: 14px;
    }

    .metric-card {
      background: #1e293b;
      border: 1px solid #334155;
      border-radius: 10px;
      padding: 16px;
      position: relative;
      transition: all 0.2s ease;
    }

    .metric-card.highlight {
      border-color: #38bdf8;
      box-shadow: 0 0 15px rgba(56, 189, 248, 0.15);
    }

    .metric-label {
      font-size: 12px;
      color: #94a3b8;
      text-transform: uppercase;
      font-weight: 600;
      letter-spacing: 0.5px;
    }

    .metric-val {
      font-size: 26px;
      font-weight: 800;
      color: #f8fafc;
      margin: 6px 0;
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .metric-hint {
      font-size: 11px;
      color: #64748b;
    }

    .text-amber { color: #f59e0b; }
    .text-cyan { color: #38bdf8; }
    .text-emerald { color: #10b981; }
    .text-purple { color: #c084fc; }

    .pulsing-dot {
      width: 10px;
      height: 10px;
      background: #ef4444;
      border-radius: 50%;
      display: inline-block;
      box-shadow: 0 0 0 0 rgba(239, 68, 68, 0.7);
      animation: pulse-ring 1.5s infinite;
    }

    @keyframes pulse-ring {
      0% { transform: scale(0.95); box-shadow: 0 0 0 0 rgba(239, 68, 68, 0.7); }
      70% { transform: scale(1); box-shadow: 0 0 0 8px rgba(239, 68, 68, 0); }
      100% { transform: scale(0.95); box-shadow: 0 0 0 0 rgba(239, 68, 68, 0); }
    }

    /* Alerts */
    .alert-notice {
      background: #064e3b;
      border: 1px solid #059669;
      color: #a7f3d0;
      padding: 10px 14px;
      border-radius: 8px;
      display: flex;
      justify-content: space-between;
      align-items: center;
      font-size: 14px;
    }

    .alert-notice.alert-error {
      background: #7f1d1d;
      border-color: #dc2626;
      color: #fecaca;
    }

    .close-btn {
      background: transparent;
      border: none;
      color: inherit;
      cursor: pointer;
      font-size: 14px;
    }

    /* Content Split */
    .content-split {
      display: grid;
      grid-template-columns: 340px 1fr;
      gap: 18px;
      align-items: start;
    }

    @media (max-width: 960px) {
      .content-split {
        grid-template-columns: 1fr;
      }
    }

    /* Left Panel: Queues */
    .queues-panel {
      background: #0f172a;
      border: 1px solid #334155;
      border-radius: 12px;
      padding: 16px;
    }

    .panel-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 14px;
    }

    .panel-header h3 {
      font-size: 16px;
      font-weight: 700;
      color: #f8fafc;
      margin: 0;
    }

    .panel-desc {
      font-size: 12px;
      color: #94a3b8;
      margin: 2px 0 0 0;
    }

    .icon-btn {
      background: #1e293b;
      border: 1px solid #334155;
      border-radius: 6px;
      padding: 4px 8px;
      cursor: pointer;
      color: #e2e8f0;
    }

    .queue-cards-list {
      display: flex;
      flex-direction: column;
      gap: 10px;
    }

    .queue-card {
      background: #1e293b;
      border: 1px solid #334155;
      border-radius: 8px;
      padding: 12px;
      cursor: pointer;
      transition: all 0.2s ease;
    }

    .queue-card:hover {
      border-color: #475569;
      transform: translateY(-1px);
    }

    .queue-card.selected {
      border-color: #38bdf8;
      background: #1e293b;
      box-shadow: 0 0 10px rgba(56, 189, 248, 0.2);
    }

    .queue-card.inactive {
      opacity: 0.6;
    }

    .queue-card-top {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 8px;
    }

    .queue-name-wrap {
      display: flex;
      align-items: center;
      gap: 6px;
    }

    .queue-name {
      font-weight: 700;
      font-size: 14px;
      color: #f1f5f9;
    }

    .badge-priority {
      background: #312e81;
      color: #a5b4fc;
      font-size: 10px;
      padding: 2px 6px;
      border-radius: 4px;
      font-weight: 700;
    }

    .status-pill {
      font-size: 10px;
      padding: 2px 6px;
      border-radius: 10px;
      font-weight: 600;
    }

    .pill-active { background: #064e3b; color: #34d399; }
    .pill-inactive { background: #451a03; color: #fbbf24; }

    .queue-card-body {
      display: flex;
      justify-content: space-between;
      align-items: center;
      font-size: 12px;
      color: #94a3b8;
    }

    .stat-pill {
      display: flex;
      align-items: center;
      gap: 6px;
    }

    .count-badge {
      background: #334155;
      color: #e2e8f0;
      padding: 2px 8px;
      border-radius: 10px;
      font-weight: 700;
      font-size: 12px;
    }

    .count-badge.count-high {
      background: #ef4444;
      color: #ffffff;
    }

    .queue-card-actions {
      display: flex;
      gap: 8px;
      margin-top: 8px;
      padding-top: 8px;
      border-top: 1px solid #334155;
    }

    .link-btn {
      background: transparent;
      border: none;
      color: #38bdf8;
      font-size: 12px;
      cursor: pointer;
      padding: 0;
    }

    .link-btn:hover { text-decoration: underline; }
    .link-btn.text-danger { color: #f87171; }

    /* Right Panel: Entries */
    .entries-panel {
      background: #0f172a;
      border: 1px solid #334155;
      border-radius: 12px;
      padding: 16px;
      min-height: 400px;
    }

    .badge-waiting {
      background: #0284c7;
      color: #f0f9ff;
      padding: 4px 10px;
      border-radius: 12px;
      font-size: 12px;
      font-weight: 600;
    }

    .table-responsive {
      overflow-x: auto;
    }

    .entries-table {
      width: 100%;
      border-collapse: collapse;
      font-size: 13px;
    }

    .entries-table th {
      text-align: left;
      color: #94a3b8;
      font-size: 11px;
      text-transform: uppercase;
      letter-spacing: 0.5px;
      padding: 10px;
      border-bottom: 1px solid #334155;
    }

    .entries-table td {
      padding: 12px 10px;
      border-bottom: 1px solid #1e293b;
      vertical-align: middle;
    }

    .entries-table tr:hover td {
      background: #1e293b;
    }

    .entries-table tr.gold-row td {
      background: rgba(245, 158, 11, 0.05);
    }

    .pos-badge {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 28px;
      height: 28px;
      border-radius: 6px;
      background: #1e293b;
      border: 1px solid #475569;
      font-weight: 800;
      color: #cbd5e1;
    }

    .pos-badge.pos-first {
      background: #b45309;
      border-color: #f59e0b;
      color: #fef3c7;
      box-shadow: 0 0 8px rgba(245, 158, 11, 0.4);
    }

    .priority-cell {
      display: flex;
      align-items: center;
      gap: 6px;
    }

    .badge {
      font-size: 11px;
      padding: 2px 6px;
      border-radius: 4px;
      background: #334155;
      color: #cbd5e1;
    }

    .badge-vip {
      background: #78350f;
      color: #fde68a;
      border: 1px solid #b45309;
      font-weight: 700;
    }

    .mini-btn {
      background: #334155;
      border: 1px solid #475569;
      color: #e2e8f0;
      border-radius: 4px;
      width: 20px;
      height: 20px;
      display: inline-flex;
      align-items: center;
      justify-content: center;
      cursor: pointer;
      font-size: 10px;
    }

    .mini-btn:hover {
      background: #475569;
      color: #38bdf8;
    }

    .caller-info {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .caller-phone {
      font-weight: 700;
      color: #f8fafc;
    }

    .caller-name {
      font-size: 11px;
      color: #94a3b8;
    }

    .timer-badge {
      background: #064e3b;
      color: #6ee7b7;
      padding: 4px 8px;
      border-radius: 6px;
      font-family: monospace;
      font-size: 12px;
      font-weight: 700;
    }

    .timer-badge.timer-warn {
      background: #78350f;
      color: #fde68a;
    }

    .timer-badge.timer-danger {
      background: #7f1d1d;
      color: #fca5a5;
    }

    .row-actions {
      display: flex;
      justify-content: flex-end;
      gap: 6px;
    }

    /* Buttons */
    .btn {
      padding: 8px 14px;
      border-radius: 6px;
      font-weight: 600;
      font-size: 13px;
      cursor: pointer;
      border: none;
      transition: all 0.2s;
    }

    .btn:disabled {
      opacity: 0.5;
      cursor: not-allowed;
    }

    .btn-primary { background: #0284c7; color: #ffffff; }
    .btn-primary:hover:not(:disabled) { background: #0369a1; }

    .btn-secondary { background: #1e293b; color: #e2e8f0; border: 1px solid #334155; }
    .btn-secondary:hover:not(:disabled) { background: #334155; }

    .btn-sm {
      padding: 4px 8px;
      font-size: 11px;
    }

    .btn-assign {
      background: #047857;
      color: #ffffff;
      border: 1px solid #059669;
    }

    .btn-assign:hover { background: #065f46; }

    .btn-cancel {
      background: #991b1b;
      color: #ffffff;
      border: 1px solid #b91c1c;
    }

    .btn-cancel:hover { background: #7f1d1d; }

    /* Modals */
    .modal-backdrop {
      position: fixed;
      inset: 0;
      background: rgba(0, 0, 0, 0.7);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1000;
      backdrop-filter: blur(2px);
    }

    .modal-card {
      background: #1e293b;
      border: 1px solid #334155;
      border-radius: 12px;
      width: 100%;
      max-width: 440px;
      padding: 20px;
      box-shadow: 0 10px 25px rgba(0, 0, 0, 0.5);
    }

    .modal-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 16px;
    }

    .modal-header h3 {
      margin: 0;
      font-size: 18px;
      color: #f8fafc;
    }

    .modal-body {
      display: flex;
      flex-direction: column;
      gap: 14px;
    }

    .modal-footer {
      display: flex;
      justify-content: flex-end;
      gap: 10px;
      margin-top: 20px;
    }

    .form-group {
      display: flex;
      flex-direction: column;
      gap: 6px;
    }

    .form-group label {
      font-size: 12px;
      color: #94a3b8;
      font-weight: 600;
    }

    .form-input {
      background: #0f172a;
      border: 1px solid #334155;
      border-radius: 6px;
      padding: 8px 12px;
      color: #f8fafc;
      font-size: 13px;
    }

    .form-input:focus {
      outline: none;
      border-color: #38bdf8;
    }

    .checkbox-label {
      display: flex;
      align-items: center;
      gap: 8px;
      cursor: pointer;
      color: #e2e8f0;
      font-size: 13px;
    }

    .agents-pick-list {
      max-height: 200px;
      overflow-y: auto;
      display: flex;
      flex-direction: column;
      gap: 6px;
    }

    .agent-pick-item {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 8px;
      border-radius: 6px;
      background: #0f172a;
      border: 1px solid #334155;
      cursor: pointer;
    }

    .agent-pick-item:hover {
      border-color: #475569;
    }

    .agent-pick-item.selected {
      border-color: #10b981;
      background: #064e3b;
    }

    .agent-avatar {
      width: 28px;
      height: 28px;
      border-radius: 50%;
      background: #0284c7;
      color: #ffffff;
      display: flex;
      align-items: center;
      justify-content: center;
      font-weight: 700;
      font-size: 12px;
    }

    .status-dot-green {
      width: 8px;
      height: 8px;
      border-radius: 50%;
      background: #10b981;
      margin-left: auto;
    }

    .empty-entries, .no-selection, .empty-state, .loading-wrap {
      text-align: center;
      padding: 40px 20px;
      color: #94a3b8;
    }

    .empty-icon {
      font-size: 36px;
      margin-bottom: 8px;
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class QueueManagementComponent implements OnInit, OnDestroy {
  private readonly queueService = inject(QueueService);
  private readonly agentService = inject(AgentService);
  private readonly realtime = inject(CallCenterRealtimeService);

  readonly queues = signal<CallQueue[]>([]);
  readonly summary = signal<QueueSummary | null>(null);
  readonly selectedQueue = signal<CallQueue | null>(null);
  readonly entries = signal<CallQueueEntry[]>([]);
  readonly availableAgents = signal<Agent[]>([]);
  readonly now = signal<number>(Date.now());

  readonly loadingEntries = signal<boolean>(false);
  readonly isAssigning = signal<boolean>(false);
  readonly notice = signal<{ message: string; isError?: boolean } | null>(null);

  // Modals state
  readonly showQueueModal = signal<boolean>(false);
  readonly editingQueueId = signal<string | null>(null);
  queueForm = { name: '', priority: 1, isActive: true };

  readonly showAssignModal = signal<boolean>(false);
  readonly assigningEntry = signal<CallQueueEntry | null>(null);
  readonly selectedAgentId = signal<string | null>(null);

  private timerSub?: Subscription;
  private realtimeSub?: Subscription;

  ngOnInit(): void {
    this.loadAll();

    // 1-second live ticker for elapsed wait timer
    this.timerSub = interval(1000).subscribe(() => {
      this.now.set(Date.now());
    });

    // Real-time events from SignalR
    this.realtimeSub = this.realtime.events$.subscribe(event => {
      if (
        event.type === 'queue-updated' ||
        event.type === 'call-status' ||
        event.type === 'agent-status' ||
        event.type === 'incoming-call'
      ) {
        this.refreshQuietly();
      }
    });
  }

  ngOnDestroy(): void {
    this.timerSub?.unsubscribe();
    this.realtimeSub?.unsubscribe();
  }

  loadAll(): void {
    this.loadSummary();
    this.loadQueues();
    this.loadAvailableAgents();
  }

  private refreshQuietly(): void {
    this.loadSummary();
    this.queueService.getQueues().subscribe(qs => {
      this.queues.set(qs);
      const current = this.selectedQueue();
      if (current) {
        const found = qs.find(q => q.id === current.id);
        if (found) {
          this.selectedQueue.set(found);
          this.loadEntries(found.id, false);
        }
      }
    });
    this.loadAvailableAgents();
  }

  loadSummary(): void {
    this.queueService.getSummary().subscribe({
      next: s => this.summary.set(s),
      error: () => {}
    });
  }

  loadQueues(): void {
    this.queueService.getQueues().subscribe({
      next: qs => {
        this.queues.set(qs);
        if (!this.selectedQueue() && qs.length > 0) {
          this.selectQueue(qs[0]);
        }
      },
      error: err => this.showNotification('Failed to load queues: ' + (err?.error?.message || err.message), true)
    });
  }

  selectQueue(q: CallQueue): void {
    this.selectedQueue.set(q);
    this.loadEntries(q.id, true);
  }

  loadEntries(queueId: string, showLoading = true): void {
    if (showLoading) this.loadingEntries.set(true);
    this.queueService.getQueueEntries(queueId).subscribe({
      next: res => {
        this.entries.set(res);
        this.loadingEntries.set(false);
      },
      error: () => this.loadingEntries.set(false)
    });
  }

  loadAvailableAgents(): void {
    this.agentService.getAll({ status: AgentStatus.Available, isActive: true }).subscribe({
      next: (list: Agent[]) => this.availableAgents.set(list),
      error: () => {}
    });
  }

  getLiveWait(entry: CallQueueEntry): number {
    const enqueuedMs = new Date(entry.enqueuedAt).getTime();
    return Math.max(0, Math.floor((this.now() - enqueuedMs) / 1000));
  }

  formatDuration(seconds: number): string {
    const s = Math.floor(seconds || 0);
    const m = Math.floor(s / 60);
    const rem = s % 60;
    return `${m.toString().padStart(2, '0')}:${rem.toString().padStart(2, '0')}`;
  }

  triggerAutoAssign(): void {
    this.isAssigning.set(true);
    this.queueService.triggerAutoAssign().subscribe({
      next: () => {
        this.isAssigning.set(false);
        this.showNotification('Automatic queue assignment triggered successfully.');
        this.refreshQuietly();
      },
      error: err => {
        this.isAssigning.set(false);
        this.showNotification('Auto-assign failed: ' + (err?.error?.message || err.message), true);
      }
    });
  }

  bumpPriority(entry: CallQueueEntry): void {
    const nextPriority = entry.priority + 1;
    this.queueService.prioritizeEntry(entry.id, nextPriority).subscribe({
      next: () => {
        this.showNotification(`Priority bumped to ${nextPriority} for call #${entry.position}.`);
        if (this.selectedQueue()) {
          this.loadEntries(this.selectedQueue()!.id, false);
        }
      },
      error: err => this.showNotification('Failed to bump priority: ' + (err?.error?.message || err.message), true)
    });
  }

  cancelCall(entry: CallQueueEntry): void {
    if (!confirm(`Cancel waiting call for ${entry.phoneNumber}? This will mark it as abandoned.`)) return;

    this.queueService.cancelCall(entry.callId, 'Cancelled by supervisor from queue board').subscribe({
      next: () => {
        this.showNotification('Call removed from queue.');
        if (this.selectedQueue()) {
          this.loadEntries(this.selectedQueue()!.id, false);
        }
        this.loadSummary();
      },
      error: err => this.showNotification('Failed to cancel call: ' + (err?.error?.message || err.message), true)
    });
  }

  openAssignModal(entry: CallQueueEntry): void {
    this.assigningEntry.set(entry);
    this.selectedAgentId.set(null);
    this.loadAvailableAgents();
    this.showAssignModal.set(true);
  }

  closeAssignModal(): void {
    this.showAssignModal.set(false);
    this.assigningEntry.set(null);
  }

  confirmDirectAssign(): void {
    const entry = this.assigningEntry();
    const agentId = this.selectedAgentId();
    if (!entry || !agentId) return;

    this.isAssigning.set(true);
    this.queueService.assignCallDirectly(entry.callId, agentId).subscribe({
      next: () => {
        this.isAssigning.set(false);
        this.closeAssignModal();
        this.showNotification('Call assigned to agent successfully.');
        this.refreshQuietly();
      },
      error: err => {
        this.isAssigning.set(false);
        this.showNotification('Direct assignment failed: ' + (err?.error?.message || err.message), true);
      }
    });
  }

  openCreateModal(): void {
    this.editingQueueId.set(null);
    this.queueForm = { name: '', priority: 1, isActive: true };
    this.showQueueModal.set(true);
  }

  openEditModal(q: CallQueue): void {
    this.editingQueueId.set(q.id);
    this.queueForm = {
      name: q.name || '',
      priority: q.priority || 1,
      isActive: q.isActive ?? true
    };
    this.showQueueModal.set(true);
  }

  closeQueueModal(): void {
    this.showQueueModal.set(false);
  }

  saveQueue(): void {
    const id = this.editingQueueId();
    if (id) {
      this.queueService.updateQueue(id, this.queueForm).subscribe({
        next: () => {
          this.closeQueueModal();
          this.showNotification('Queue updated successfully.');
          this.loadQueues();
        },
        error: err => this.showNotification('Failed to update queue: ' + (err?.error?.message || err.message), true)
      });
    } else {
      this.queueService.createQueue(this.queueForm).subscribe({
        next: created => {
          this.closeQueueModal();
          this.showNotification(`Queue '${created.name}' created successfully.`);
          this.loadQueues();
        },
        error: err => this.showNotification('Failed to create queue: ' + (err?.error?.message || err.message), true)
      });
    }
  }

  deleteQueue(q: CallQueue): void {
    if (!confirm(`Deactivate queue '${q.name}'?`)) return;

    this.queueService.deleteQueue(q.id).subscribe({
      next: () => {
        this.showNotification('Queue deactivated.');
        this.loadQueues();
      },
      error: err => this.showNotification('Failed: ' + (err?.error?.message || err.message), true)
    });
  }

  private showNotification(message: string, isError = false): void {
    this.notice.set({ message, isError });
    setTimeout(() => this.clearNotice(), 6000);
  }

  clearNotice(): void {
    this.notice.set(null);
  }
}
