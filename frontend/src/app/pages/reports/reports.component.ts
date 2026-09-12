import { ChangeDetectionStrategy, ChangeDetectorRef, Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { ReportsService } from '../../core/services/reports.service';
import { DispositionService } from '../../core/services/disposition.service';
import { AuthService } from '../../core/auth/auth.service';
import { ComprehensiveAnalyticsReport } from '../../core/models/call-history.models';
import { CallDisposition, CreateDispositionRequest, UpdateDispositionRequest } from '../../core/models/disposition.models';

@Component({
  selector: 'app-reports',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="reports-page">
      <!-- Page Header -->
      <div class="page-header">
        <div>
          <span class="eyebrow">Enterprise Telemetry & Intelligence</span>
          <h2>Reporting & Operational Analytics</h2>
          <p>Real-time analytics, directional call mix, agent productivity, queue latency, and disposition outcomes.</p>
        </div>
        <div class="header-actions">
          <div class="tab-switcher">
            <button
              type="button"
              class="tab-btn"
              [class.active]="activeTab === 'analytics'"
              (click)="setActiveTab('analytics')">
              📊 Analytics & Charts
            </button>
            @if (canManageDispositions()) {
              <button
                type="button"
                class="tab-btn"
                [class.active]="activeTab === 'config'"
                (click)="setActiveTab('config')">
                ⚙️ Disposition Setup
              </button>
            }
          </div>

          <button class="button secondary" [disabled]="loading" (click)="refreshCurrentTab()">
            <span [class.spinning]="loading">↻</span> Refresh
          </button>

          @if (activeTab === 'analytics') {
            <button class="button primary" [disabled]="loading || exporting" (click)="exportCsv()">
              <span>📥</span> {{ exporting ? 'Exporting...' : 'Export CSV' }}
            </button>
          }
        </div>
      </div>

      <!-- TAB 1: ANALYTICS & CHARTS -->
      @if (activeTab === 'analytics') {
        <!-- Filter Toolbar -->
        <section class="filter-card">
          <div class="filter-header">
            <div class="preset-group">
              <span class="filter-title">Timeframe:</span>
              <button
                type="button"
                class="preset-btn"
                [class.active]="selectedPreset === 'today'"
                (click)="selectPreset('today')">
                Today
              </button>
              <button
                type="button"
                class="preset-btn"
                [class.active]="selectedPreset === 'yesterday'"
                (click)="selectPreset('yesterday')">
                Yesterday
              </button>
              <button
                type="button"
                class="preset-btn"
                [class.active]="selectedPreset === 'thisweek'"
                (click)="selectPreset('thisweek')">
                This Week
              </button>
              <button
                type="button"
                class="preset-btn"
                [class.active]="selectedPreset === 'thismonth'"
                (click)="selectPreset('thismonth')">
                This Month
              </button>
              <button
                type="button"
                class="preset-btn"
                [class.active]="selectedPreset === 'custom'"
                (click)="selectPreset('custom')">
                Custom Range
              </button>
            </div>

            @if (selectedPreset === 'custom') {
              <div class="custom-range-group">
                <label>
                  <span>From</span>
                  <input type="date" [(ngModel)]="customFromDate" (change)="onCustomDateChange()">
                </label>
                <label>
                  <span>To</span>
                  <input type="date" [(ngModel)]="customToDate" (change)="onCustomDateChange()">
                </label>
                <button type="button" class="button secondary btn-sm" (click)="loadAnalytics()">
                  Apply Range
                </button>
              </div>
            }
          </div>

          @if (analytics) {
            <div class="filter-status-bar">
              <span class="range-indicator">
                🕒 Active Period: <strong>{{ analytics.periodPreset }}</strong>
                ({{ analytics.fromUtc | date:'medium' }} — {{ analytics.toUtc | date:'medium' }})
              </span>
              <span class="gen-indicator">Telemetry synced at {{ analytics.generatedAt | date:'shortTime' }}</span>
            </div>
          }
        </section>

        @if (error) {
          <div class="error-banner">
            <span>⚠️ Unable to compile telemetry aggregations from the backend database.</span>
            <button class="button secondary btn-sm" (click)="loadAnalytics()">Retry</button>
          </div>
        }

        @if (loading && !analytics) {
          <div class="state-card loading-state">
            <div class="spinner"></div>
            <p>Compiling database aggregations and time-series telemetry…</p>
          </div>
        } @else if (analytics) {
          <!-- KPI METRIC CARDS -->
          <div class="metric-grid">
            <article class="metric-card">
              <span class="card-label">Total Call Volume</span>
              <strong class="card-value">{{ analytics.volume.totalCalls }}</strong>
              <span class="card-sub">Inbound & Outbound traffic</span>
            </article>

            <article class="metric-card highlight-success">
              <span class="card-label">Completed Calls</span>
              <strong class="card-value">{{ analytics.volume.completed }}</strong>
              <span class="card-sub">
                {{ getCompletionRate() }}% resolution rate
              </span>
            </article>

            <article class="metric-card">
              <span class="card-label">Inbound Calls</span>
              <strong class="card-value">{{ analytics.volume.incoming }}</strong>
              <span class="card-sub">Customer initiated</span>
            </article>

            <article class="metric-card">
              <span class="card-label">Outbound Calls</span>
              <strong class="card-value">{{ analytics.volume.outgoing }}</strong>
              <span class="card-sub">Agent initiated</span>
            </article>

            <article class="metric-card highlight-danger">
              <span class="card-label">Missed & Rejected</span>
              <strong class="card-value">{{ analytics.volume.missed + analytics.volume.rejected }}</strong>
              <span class="card-sub">{{ analytics.volume.missed }} abandoned · {{ analytics.volume.rejected }} rejected</span>
            </article>

            <article class="metric-card">
              <span class="card-label">Average Talk Duration</span>
              <strong class="card-value">{{ formatDuration(analytics.volume.averageDurationSeconds) }}</strong>
              <span class="card-sub">Per handled call</span>
            </article>

            <article class="metric-card highlight-info">
              <span class="card-label">Total Talk Time</span>
              <strong class="card-value">{{ formatLongDuration(analytics.volume.totalTalkTimeSeconds) }}</strong>
              <span class="card-sub">Cumulative voice time</span>
            </article>
          </div>

          <!-- CHARTS ROW 1: CALLS OVER TIME & DIRECTION -->
          <div class="charts-row">
            <!-- Calls Over Time Chart -->
            <section class="card chart-card flex-2">
              <div class="section-title">
                <div>
                  <h3>📈 Call Volume Over Time</h3>
                  <p>Chronological distribution of total, completed, and missed calls across the timeframe.</p>
                </div>
              </div>

              @if (!analytics.charts.callsOverTime.length) {
                <div class="empty-chart">No call trend activity recorded in this period.</div>
              } @else {
                <div class="bar-chart-container">
                  <div class="bar-chart">
                    @for (pt of analytics.charts.callsOverTime; track pt.timestamp) {
                      <div class="bar-col" [title]="pt.periodLabel + ': ' + pt.total + ' calls (' + pt.completed + ' completed)'">
                        <div class="bar-stack">
                          @if (pt.total > 0) {
                            <div
                              class="bar-segment segment-completed"
                              [style.height.%]="getBarHeight(pt.completed, maxTrendTotal)">
                            </div>
                            <div
                              class="bar-segment segment-missed"
                              [style.height.%]="getBarHeight(pt.missed + pt.rejected, maxTrendTotal)">
                            </div>
                          } @else {
                            <div class="bar-empty"></div>
                          }
                        </div>
                        <span class="bar-label">{{ pt.periodLabel }}</span>
                      </div>
                    }
                  </div>
                  <div class="chart-legend">
                    <span class="legend-item"><span class="legend-color completed"></span> Completed</span>
                    <span class="legend-item"><span class="legend-color missed"></span> Missed / Rejected</span>
                  </div>
                </div>
              }
            </section>

            <!-- Calls by Direction Donut Chart -->
            <section class="card chart-card flex-1">
              <div class="section-title">
                <div>
                  <h3>🧭 Calls by Direction</h3>
                  <p>Inbound vs Outbound traffic split.</p>
                </div>
              </div>

              <div class="donut-chart-container">
                <svg class="donut-svg" viewBox="0 0 100 100">
                  <circle class="donut-bg" cx="50" cy="50" r="40"></circle>
                  <circle
                    class="donut-segment inbound-segment"
                    cx="50"
                    cy="50"
                    r="40"
                    [style.stroke-dasharray]="getInboundDashArray()"
                    stroke-dashoffset="25">
                  </circle>
                  <text x="50" y="47" class="donut-center-total">{{ analytics.volume.totalCalls }}</text>
                  <text x="50" y="58" class="donut-center-sub">Total</text>
                </svg>

                <div class="donut-legend">
                  <div class="donut-legend-row">
                    <div class="legend-tag-dot inbound"></div>
                    <span class="legend-label">Inbound (Incoming)</span>
                    <strong class="legend-val">{{ analytics.volume.incoming }} ({{ getDirPct('inbound') }}%)</strong>
                  </div>
                  <div class="donut-legend-row">
                    <div class="legend-tag-dot outbound"></div>
                    <span class="legend-label">Outbound (Outgoing)</span>
                    <strong class="legend-val">{{ analytics.volume.outgoing }} ({{ getDirPct('outbound') }}%)</strong>
                  </div>
                </div>
              </div>
            </section>
          </div>

          <!-- CHARTS ROW 2: STATUS & DISPOSITION STATS -->
          <div class="charts-row">
            <!-- Calls by Status -->
            <section class="card chart-card flex-1">
              <div class="section-title">
                <div>
                  <h3>📊 Calls by Status</h3>
                  <p>End state lifecycle distribution.</p>
                </div>
              </div>

              @if (!analytics.charts.callsByStatus.length) {
                <div class="empty-chart">No calls recorded.</div>
              } @else {
                <div class="status-bars-list">
                  @for (st of analytics.charts.callsByStatus; track st.key) {
                    <div class="status-bar-row">
                      <div class="status-bar-meta">
                        <span class="status-bar-label">{{ st.label }}</span>
                        <strong>{{ st.count }} <small>({{ st.percentage }}%)</small></strong>
                      </div>
                      <div class="status-progress-track">
                        <div
                          class="status-progress-fill"
                          [ngClass]="'status-bg-' + st.key.toLowerCase()"
                          [style.width.%]="st.percentage">
                        </div>
                      </div>
                    </div>
                  }
                </div>
              }
            </section>

            <!-- Calls by Disposition -->
            <section class="card chart-card flex-1">
              <div class="section-title">
                <div>
                  <h3>🏷️ Calls by Business Outcome</h3>
                  <p>Resolution distribution across dispositions.</p>
                </div>
              </div>

              @if (!analytics.charts.callsByDisposition.length) {
                <div class="empty-chart">No call dispositions logged in this period.</div>
              } @else {
                <div class="status-bars-list">
                  @for (disp of analytics.charts.callsByDisposition; track disp.key) {
                    <div class="status-bar-row">
                      <div class="status-bar-meta">
                        <span class="status-bar-label">
                          {{ getOutcomeIcon(disp.key) }} {{ disp.label }}
                        </span>
                        <strong>{{ disp.count }} <small>({{ disp.percentage }}%)</small></strong>
                      </div>
                      <div class="status-progress-track">
                        <div
                          class="status-progress-fill disposition-fill"
                          [style.width.%]="disp.percentage">
                        </div>
                      </div>
                    </div>
                  }
                </div>
              }
            </section>
          </div>

          <!-- AGENT PERFORMANCE SECTION -->
          <section class="card data-section-card">
            <div class="section-title split-title">
              <div>
                <h3>🎧 Agent Performance & Productivity</h3>
                <p>Volume, resolution rates, talk time, and response efficiency by team member.</p>
              </div>
              <span class="badge-count">{{ analytics.agents.length }} Agents Tracked</span>
            </div>

            @if (!analytics.agents.length) {
              <div class="empty-table-state">No agent activity logged for this period.</div>
            } @else {
              <div class="table-container">
                <table class="data-table">
                  <thead>
                    <tr>
                      <th>Agent Name</th>
                      <th>Code</th>
                      <th>Team</th>
                      <th>Total Calls</th>
                      <th>Completed</th>
                      <th>Missed</th>
                      <th>Rejected</th>
                      <th>Total Talk Time</th>
                      <th>Avg Talk Time</th>
                      <th>Completion %</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (ag of analytics.agents; track ag.agentId) {
                      <tr>
                        <td class="font-bold">👤 {{ ag.agentName }}</td>
                        <td><span class="mono-code">{{ ag.employeeCode }}</span></td>
                        <td>{{ ag.team || 'General' }}</td>
                        <td><strong>{{ ag.totalCalls }}</strong></td>
                        <td class="text-success"><strong>{{ ag.completedCalls }}</strong></td>
                        <td class="text-warning">{{ ag.missedCalls }}</td>
                        <td class="text-danger">{{ ag.rejectedCalls }}</td>
                        <td>{{ formatLongDuration(ag.totalTalkTimeSeconds) }}</td>
                        <td>{{ formatDuration(ag.averageTalkTimeSeconds) }}</td>
                        <td>
                          <div class="rate-badge" [class.rate-high]="ag.completionRatePercent >= 70" [class.rate-mid]="ag.completionRatePercent < 70 && ag.completionRatePercent >= 40" [class.rate-low]="ag.completionRatePercent < 40">
                            {{ ag.completionRatePercent | number:'1.1-1' }}%
                          </div>
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            }
          </section>

          <!-- QUEUE PERFORMANCE SECTION -->
          <section class="card data-section-card">
            <div class="section-title split-title">
              <div>
                <h3>⏳ Inbound Queue Performance</h3>
                <p>Waiting queue latency, abandonment rates, and throughput metrics.</p>
              </div>
              <span class="badge-count">{{ analytics.queues.length }} Queues Monitored</span>
            </div>

            @if (!analytics.queues.length) {
              <div class="empty-table-state">No queue traffic recorded for this period.</div>
            } @else {
              <div class="table-container">
                <table class="data-table">
                  <thead>
                    <tr>
                      <th>Queue Name</th>
                      <th>Total Enqueued</th>
                      <th>Answered / Serviced</th>
                      <th>Abandoned in Queue</th>
                      <th>Average Wait Time</th>
                      <th>Max Wait Time</th>
                      <th>Currently Waiting</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (q of analytics.queues; track q.queueId) {
                      <tr>
                        <td class="font-bold">📌 {{ q.queueName }}</td>
                        <td><strong>{{ q.totalEnqueued }}</strong></td>
                        <td class="text-success"><strong>{{ q.answeredCalls }}</strong></td>
                        <td class="text-danger">{{ q.abandonedCalls }}</td>
                        <td>{{ formatDuration(q.averageWaitSeconds) }}</td>
                        <td>{{ formatDuration(q.maxWaitSeconds) }}</td>
                        <td>
                          <span class="pill" [class.pill-warning]="q.currentWaiting > 0" [class.pill-success]="q.currentWaiting === 0">
                            {{ q.currentWaiting }} Waiting
                          </span>
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            }
          </section>

          <!-- DETAILED DISPOSITIONS TABLE -->
          <section class="card data-section-card">
            <div class="section-title split-title">
              <div>
                <h3>📋 Call Outcome Statistics</h3>
                <p>Granular business outcomes and follow-up commitments.</p>
              </div>
              <span class="badge-count">{{ analytics.dispositions.length }} Outcomes</span>
            </div>

            @if (!analytics.dispositions.length) {
              <div class="empty-table-state">No disposition outcomes recorded.</div>
            } @else {
              <div class="table-container">
                <table class="data-table">
                  <thead>
                    <tr>
                      <th>Outcome / Disposition</th>
                      <th>Code</th>
                      <th>Call Count</th>
                      <th>Percentage</th>
                      <th>Total Talk Time</th>
                      <th>Avg Duration</th>
                      <th>Follow-Up Required</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (disp of analytics.dispositions; track disp.dispositionId) {
                      <tr>
                        <td class="font-bold">
                          {{ getOutcomeIcon(disp.dispositionCode) }} {{ disp.dispositionName }}
                        </td>
                        <td><span class="mono-code">{{ disp.dispositionCode }}</span></td>
                        <td><strong>{{ disp.callCount }}</strong></td>
                        <td>
                          <div class="pct-cell">
                            <span>{{ disp.percentage | number:'1.1-1' }}%</span>
                            <div class="mini-progress-track">
                              <div class="mini-progress-fill" [style.width.%]="disp.percentage"></div>
                            </div>
                          </div>
                        </td>
                        <td>{{ formatLongDuration(disp.totalTalkTimeSeconds) }}</td>
                        <td>{{ formatDuration(disp.averageDurationSeconds) }}</td>
                        <td>
                          @if (disp.requiresFollowUp) {
                            <span class="tag tag-warning">Follow-Up Required</span>
                          } @else {
                            <span class="tag tag-muted">Standard</span>
                          }
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            }
          </section>
        }
      }

      <!-- TAB 2: DISPOSITION CONFIGURATION -->
      @if (activeTab === 'config') {
        <section class="card config-card">
          <div class="config-header">
            <div>
              <h3>Call Disposition Setup</h3>
              <p>Configure standardized business outcomes used by agents during call wrap-up.</p>
            </div>
            <div class="config-actions">
              <label class="toggle-label">
                <input
                  type="checkbox"
                  [checked]="includeInactiveDispositions"
                  (change)="toggleIncludeInactive()">
                <span>Show Inactive</span>
              </label>
              <button class="button primary" (click)="openCreateModal()">
                + Create New Disposition
              </button>
            </div>
          </div>

          @if (configActionMessage) {
            <div class="success-banner">
              <span>✅ {{ configActionMessage }}</span>
              <button (click)="configActionMessage = ''">×</button>
            </div>
          }

          @if (configErrorMessage) {
            <div class="error-banner">
              <span>⚠️ {{ configErrorMessage }}</span>
              <button (click)="configErrorMessage = ''">×</button>
            </div>
          }

          @if (loadingConfig) {
            <div class="loading-state">
              <div class="spinner"></div>
              <p>Loading disposition registry…</p>
            </div>
          } @else {
            <div class="table-container">
              <table class="data-table">
                <thead>
                  <tr>
                    <th>Icon</th>
                    <th>Code</th>
                    <th>Name</th>
                    <th>Description</th>
                    <th>Order</th>
                    <th>Flags</th>
                    <th>Status</th>
                    <th>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  @for (disp of allDispositions; track disp.id) {
                    <tr [class.inactive-row]="!disp.isActive">
                      <td class="disp-icon-col">{{ getOutcomeIcon(disp.code) }}</td>
                      <td><span class="mono-code">{{ disp.code }}</span></td>
                      <td class="font-bold">{{ disp.name }}</td>
                      <td class="desc-cell">{{ disp.description || '—' }}</td>
                      <td>{{ disp.sortOrder }}</td>
                      <td>
                        <div class="tag-group">
                          @if (disp.requiresFollowUp) { <span class="tag tag-warning">Follow-Up</span> }
                          @if (disp.requiresNotes) { <span class="tag tag-info">Notes</span> }
                          @if (!disp.requiresFollowUp && !disp.requiresNotes) { <span class="tag tag-muted">Standard</span> }
                        </div>
                      </td>
                      <td>
                        <span class="pill" [class.pill-success]="disp.isActive" [class.pill-muted]="!disp.isActive">
                          {{ disp.isActive ? 'Active' : 'Inactive' }}
                        </span>
                      </td>
                      <td>
                        <div class="row-actions">
                          <button class="mini-btn" (click)="openEditModal(disp)">Edit</button>
                          <button
                            class="mini-btn"
                            [class.deactivate-btn]="disp.isActive"
                            (click)="toggleDispositionStatus(disp)">
                            {{ disp.isActive ? 'Deactivate' : 'Activate' }}
                          </button>
                        </div>
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        </section>
      }
    </div>

    <!-- MODAL FOR DISPOSITION CONFIG -->
    @if (editModalOpen) {
      <div class="modal-backdrop" (click)="closeEditModal()">
        <div class="modal-card" (click)="$event.stopPropagation()">
          <div class="modal-header">
            <h3>{{ isEditing ? 'Edit Disposition' : 'Create New Disposition' }}</h3>
            <button class="modal-close" (click)="closeEditModal()">×</button>
          </div>
          <div class="modal-body">
            @if (modalErrorMessage) {
              <div class="modal-error">{{ modalErrorMessage }}</div>
            }
            <div class="form-group">
              <label>Disposition Code <span class="required">*</span></label>
              <input
                type="text"
                class="form-control"
                [(ngModel)]="editFormCode"
                [disabled]="isEditing"
                placeholder="e.g. ORDER_COMPLETED"
                required>
            </div>
            <div class="form-group">
              <label>Name / Label <span class="required">*</span></label>
              <input
                type="text"
                class="form-control"
                [(ngModel)]="editFormName"
                placeholder="e.g. Order Completed Successfully"
                required>
            </div>
            <div class="form-group">
              <label>Description</label>
              <textarea
                class="form-control"
                rows="3"
                [(ngModel)]="editFormDescription"
                placeholder="Operational purpose of this disposition outcome">
              </textarea>
            </div>
            <div class="form-group">
              <label>Display Sort Order</label>
              <input
                type="number"
                class="form-control"
                [(ngModel)]="editFormSortOrder">
            </div>
            <div class="checkbox-group">
              <label class="checkbox-label">
                <input type="checkbox" [(ngModel)]="editFormRequiresFollowUp">
                <span>Requires Scheduled Follow-Up Date & Time</span>
              </label>
              <label class="checkbox-label">
                <input type="checkbox" [(ngModel)]="editFormRequiresNotes">
                <span>Requires Detailed Call Wrap-Up Notes</span>
              </label>
              @if (isEditing) {
                <label class="checkbox-label">
                  <input type="checkbox" [(ngModel)]="editFormIsActive">
                  <span>Active</span>
                </label>
              }
            </div>
          </div>
          <div class="modal-footer">
            <button class="secondary-btn" [disabled]="submittingForm" (click)="closeEditModal()">Cancel</button>
            <button class="button primary" [disabled]="submittingForm" (click)="saveDisposition()">
              {{ submittingForm ? 'Saving…' : 'Save Disposition' }}
            </button>
          </div>
        </div>
      </div>
    }
  `,
  styles: [`
    :host {
      display: block;
    }

    .reports-page {
      display: flex;
      flex-direction: column;
      gap: 20px;
    }

    .page-header {
      display: flex;
      justify-content: space-between;
      align-items: flex-end;
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

    .tab-switcher {
      display: flex;
      background: #e2e8f0;
      padding: 3px;
      border-radius: 10px;
    }

    .tab-btn {
      padding: 6px 14px;
      border: none;
      background: transparent;
      border-radius: 8px;
      font-size: 13px;
      font-weight: 700;
      color: #475569;
      cursor: pointer;
      transition: all 0.15s;
    }

    .tab-btn.active {
      background: #fff;
      color: #0f172a;
      box-shadow: 0 1px 3px rgba(0, 0, 0, 0.08);
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
      transition: all 0.15s;
    }

    .button.primary {
      background: #4f46e5;
      color: #fff;
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

    .btn-sm {
      height: 32px;
      padding: 0 12px;
      font-size: 12px;
    }

    .spinning {
      display: inline-block;
      animation: spin 0.8s linear infinite;
    }

    /* Filter Card */
    .filter-card {
      background: #ffffff;
      border: 1px solid #e2e8f0;
      border-radius: 16px;
      padding: 16px 20px;
      box-shadow: 0 1px 3px rgba(0, 0, 0, 0.04);
      display: flex;
      flex-direction: column;
      gap: 12px;
    }

    .filter-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      flex-wrap: wrap;
      gap: 14px;
    }

    .preset-group {
      display: flex;
      align-items: center;
      gap: 8px;
      flex-wrap: wrap;
    }

    .filter-title {
      font-size: 13px;
      font-weight: 700;
      color: #475569;
      margin-right: 4px;
    }

    .preset-btn {
      padding: 6px 14px;
      border: 1px solid #cbd5e1;
      border-radius: 8px;
      background: #f8fafc;
      color: #334155;
      font-size: 12px;
      font-weight: 700;
      cursor: pointer;
      transition: all 0.15s;
    }

    .preset-btn:hover {
      background: #e2e8f0;
    }

    .preset-btn.active {
      background: #4f46e5;
      border-color: #4f46e5;
      color: #ffffff;
    }

    .custom-range-group {
      display: flex;
      align-items: center;
      gap: 10px;
    }

    .custom-range-group label {
      display: flex;
      align-items: center;
      gap: 6px;
      font-size: 12px;
      font-weight: 700;
      color: #475569;
    }

    .custom-range-group input {
      height: 32px;
      padding: 0 8px;
      border: 1px solid #cbd5e1;
      border-radius: 6px;
      font-size: 12px;
      color: #0f172a;
    }

    .filter-status-bar {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding-top: 10px;
      border-top: 1px solid #f1f5f9;
      font-size: 12px;
      color: #64748b;
    }

    .filter-status-bar strong {
      color: #0f172a;
    }

    /* KPI Cards */
    .metric-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
      gap: 14px;
    }

    .metric-card {
      background: #ffffff;
      border: 1px solid #e2e8f0;
      border-radius: 14px;
      padding: 16px;
      box-shadow: 0 1px 3px rgba(0, 0, 0, 0.04);
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

    .card-label {
      font-size: 11px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.06em;
      color: #64748b;
    }

    .card-value {
      font-size: 24px;
      font-weight: 800;
      color: #0f172a;
      line-height: 1.2;
    }

    .card-sub {
      font-size: 11px;
      color: #94a3b8;
    }

    .highlight-success strong { color: #16a34a; }
    .highlight-danger strong { color: #dc2626; }
    .highlight-info strong { color: #4f46e5; }

    /* Chart Rows */
    .charts-row {
      display: flex;
      gap: 20px;
    }

    .flex-1 { flex: 1; min-width: 300px; }
    .flex-2 { flex: 2; min-width: 450px; }

    .card {
      background: #ffffff;
      border: 1px solid #e2e8f0;
      border-radius: 16px;
      padding: 22px;
      box-shadow: 0 1px 3px rgba(0, 0, 0, 0.04);
    }

    .section-title {
      margin-bottom: 18px;
    }

    .section-title h3 {
      margin: 0 0 4px 0;
      font-size: 16px;
      font-weight: 800;
      color: #0f172a;
    }

    .section-title p {
      margin: 0;
      font-size: 12.5px;
      color: #64748b;
    }

    .split-title {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
    }

    .badge-count {
      font-size: 11.5px;
      font-weight: 700;
      background: #f1f5f9;
      color: #475569;
      padding: 4px 10px;
      border-radius: 999px;
    }

    /* Bar Chart Over Time */
    .bar-chart-container {
      display: flex;
      flex-direction: column;
      gap: 14px;
    }

    .bar-chart {
      display: flex;
      align-items: flex-end;
      height: 180px;
      gap: 10px;
      padding: 10px 0;
      border-bottom: 2px solid #e2e8f0;
      overflow-x: auto;
    }

    .bar-col {
      flex: 1;
      min-width: 38px;
      height: 100%;
      display: flex;
      flex-direction: column;
      justify-content: flex-end;
      align-items: center;
      gap: 6px;
    }

    .bar-stack {
      width: 100%;
      height: 100%;
      display: flex;
      flex-direction: column;
      justify-content: flex-end;
      gap: 2px;
      position: relative;
    }

    .bar-segment {
      width: 100%;
      border-radius: 4px;
      transition: height 0.3s ease;
    }

    .segment-completed {
      background: #4f46e5;
    }

    .segment-missed {
      background: #ef4444;
    }

    .bar-empty {
      height: 2px;
      background: #e2e8f0;
      width: 100%;
    }

    .bar-label {
      font-size: 10.5px;
      color: #64748b;
      white-space: nowrap;
    }

    .chart-legend {
      display: flex;
      justify-content: center;
      gap: 20px;
      font-size: 12px;
      color: #475569;
    }

    .legend-item {
      display: flex;
      align-items: center;
      gap: 6px;
    }

    .legend-color {
      width: 12px;
      height: 12px;
      border-radius: 3px;
    }

    .legend-color.completed { background: #4f46e5; }
    .legend-color.missed { background: #ef4444; }

    /* Donut Chart */
    .donut-chart-container {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 16px;
    }

    .donut-svg {
      width: 140px;
      height: 140px;
      transform: rotate(-90deg);
    }

    .donut-bg {
      fill: none;
      stroke: #f3e8ff;
      stroke-width: 14;
    }

    .donut-segment {
      fill: none;
      stroke-width: 14;
      stroke-linecap: round;
      transition: stroke-dasharray 0.3s ease;
    }

    .inbound-segment {
      stroke: #0284c7;
    }

    .donut-center-total {
      transform: rotate(90deg);
      transform-origin: center;
      font-size: 18px;
      font-weight: 800;
      fill: #0f172a;
      text-anchor: middle;
    }

    .donut-center-sub {
      transform: rotate(90deg);
      transform-origin: center;
      font-size: 9px;
      fill: #64748b;
      text-anchor: middle;
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }

    .donut-legend {
      width: 100%;
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .donut-legend-row {
      display: flex;
      align-items: center;
      justify-content: space-between;
      font-size: 12.5px;
    }

    .legend-tag-dot {
      width: 10px;
      height: 10px;
      border-radius: 50%;
      margin-right: 6px;
    }

    .legend-tag-dot.inbound { background: #0284c7; }
    .legend-tag-dot.outbound { background: #a855f7; }

    .legend-label {
      flex-grow: 1;
      color: #64748b;
    }

    .legend-val {
      color: #0f172a;
    }

    /* Status & Disposition Horizontal Bars */
    .status-bars-list {
      display: flex;
      flex-direction: column;
      gap: 12px;
    }

    .status-bar-row {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

    .status-bar-meta {
      display: flex;
      justify-content: space-between;
      font-size: 12px;
      color: #334155;
    }

    .status-bar-label {
      font-weight: 600;
    }

    .status-progress-track {
      height: 8px;
      background: #f1f5f9;
      border-radius: 9999px;
      overflow: hidden;
      width: 100%;
    }

    .status-progress-fill {
      height: 100%;
      border-radius: 9999px;
      background: #4f46e5;
    }

    .status-bg-completed { background: #16a34a; }
    .status-bg-connected { background: #4f46e5; }
    .status-bg-onhold { background: #f59e0b; }
    .status-bg-ringing { background: #ea580c; }
    .status-bg-queued { background: #94a3b8; }
    .status-bg-abandoned { background: #ef4444; }
    .status-bg-rejected { background: #dc2626; }

    .disposition-fill {
      background: linear-gradient(90deg, #0284c7, #38bdf8);
    }

    /* Tables */
    .table-container {
      overflow-x: auto;
    }

    .data-table {
      width: 100%;
      border-collapse: collapse;
      font-size: 13px;
    }

    .data-table th {
      text-align: left;
      padding: 10px 14px;
      border-bottom: 1px solid #e2e8f0;
      background: #f8fafc;
      color: #475569;
      font-size: 11px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }

    .data-table td {
      padding: 12px 14px;
      border-bottom: 1px solid #f1f5f9;
      color: #1e293b;
      vertical-align: middle;
    }

    .font-bold {
      font-weight: 700;
      color: #0f172a;
    }

    .mono-code {
      font-family: monospace;
      font-size: 11.5px;
      background: #f1f5f9;
      padding: 2px 6px;
      border-radius: 4px;
      color: #475569;
    }

    .rate-badge {
      display: inline-block;
      font-weight: 700;
      padding: 2px 8px;
      border-radius: 6px;
      font-size: 11.5px;
    }

    .rate-high { background: #dcfce7; color: #15803d; }
    .rate-mid { background: #fef3c7; color: #b45309; }
    .rate-low { background: #fee2e2; color: #b91c1c; }

    .pct-cell {
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .mini-progress-track {
      width: 80px;
      height: 6px;
      background: #f1f5f9;
      border-radius: 9999px;
      overflow: hidden;
    }

    .mini-progress-fill {
      height: 100%;
      background: #0284c7;
      border-radius: 9999px;
    }

    .text-success { color: #16a34a; }
    .text-warning { color: #f59e0b; }
    .text-danger { color: #dc2626; }

    .pill {
      display: inline-flex;
      padding: 3px 8px;
      border-radius: 999px;
      font-size: 11px;
      font-weight: 700;
    }

    .pill-success { background: #dcfce7; color: #15803d; }
    .pill-warning { background: #fef3c7; color: #b45309; }
    .pill-muted { background: #f1f5f9; color: #64748b; }

    .tag {
      font-size: 11px;
      font-weight: 700;
      padding: 2px 8px;
      border-radius: 4px;
    }

    .tag-warning { background: #fffbeb; color: #b45309; border: 1px solid #fde68a; }
    .tag-info { background: #e0f2fe; color: #0369a1; border: 1px solid #bae6fd; }
    .tag-muted { background: #f1f5f9; color: #64748b; }

    /* Configuration styles */
    .config-card {
      display: flex;
      flex-direction: column;
      gap: 16px;
    }

    .config-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      flex-wrap: wrap;
      gap: 14px;
    }

    .config-header h3 {
      margin: 0 0 4px 0;
      font-size: 18px;
      font-weight: 800;
      color: #0f172a;
    }

    .config-header p {
      margin: 0;
      font-size: 13px;
      color: #64748b;
    }

    .config-actions {
      display: flex;
      align-items: center;
      gap: 14px;
    }

    .toggle-label {
      display: flex;
      align-items: center;
      gap: 6px;
      font-size: 12.5px;
      color: #475569;
      cursor: pointer;
    }

    .success-banner {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 10px 14px;
      border-radius: 8px;
      background: #f0fdf4;
      border: 1px solid #bbf7d0;
      color: #166534;
      font-size: 13px;
    }

    .error-banner {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 12px 16px;
      border-radius: 10px;
      background: #fef2f2;
      border: 1px solid #fecaca;
      color: #991b1b;
      font-size: 13px;
    }

    .empty-chart, .empty-table-state {
      padding: 30px;
      text-align: center;
      color: #94a3b8;
      font-size: 13px;
    }

    .loading-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 12px;
      padding: 60px 20px;
      color: #64748b;
    }

    .spinner {
      width: 32px;
      height: 32px;
      border: 3px solid #e2e8f0;
      border-top-color: #4f46e5;
      border-radius: 50%;
      animation: spin 0.8s linear infinite;
    }

    @keyframes spin { to { transform: rotate(360deg); } }

    .inactive-row { opacity: 0.6; }
    .row-actions { display: flex; gap: 6px; }
    .mini-btn {
      padding: 4px 8px;
      font-size: 11.5px;
      font-weight: 600;
      background: #f1f5f9;
      border: 1px solid #cbd5e1;
      border-radius: 5px;
      cursor: pointer;
      color: #334155;
    }
    .mini-btn:hover { background: #e2e8f0; }
    .deactivate-btn { color: #b91c1c; }

    /* Modal styles */
    .modal-backdrop {
      position: fixed;
      inset: 0;
      background: rgba(15, 23, 42, 0.5);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1000;
      backdrop-filter: blur(2px);
    }
    .modal-card {
      background: #fff;
      border-radius: 16px;
      width: 90%;
      max-width: 520px;
      box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.1);
      overflow: hidden;
    }
    .modal-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 16px 20px;
      border-bottom: 1px solid #e2e8f0;
    }
    .modal-header h3 {
      margin: 0;
      font-size: 16px;
      font-weight: 800;
      color: #0f172a;
    }
    .modal-close {
      background: transparent;
      border: none;
      font-size: 20px;
      cursor: pointer;
      color: #94a3b8;
    }
    .modal-body {
      padding: 20px;
      display: flex;
      flex-direction: column;
      gap: 14px;
    }
    .form-group label {
      display: block;
      font-size: 12px;
      font-weight: 700;
      color: #334155;
      margin-bottom: 4px;
    }
    .required { color: #dc2626; font-size: 11px; }
    .form-control {
      width: 100%;
      box-sizing: border-box;
      padding: 8px 12px;
      border: 1px solid #cbd5e1;
      border-radius: 8px;
      font-size: 13px;
      color: #1e293b;
      font-family: inherit;
    }
    .checkbox-group {
      display: flex;
      flex-direction: column;
      gap: 8px;
      padding: 8px 0;
    }
    .checkbox-label {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 12.5px;
      color: #334155;
      cursor: pointer;
    }
    .modal-error {
      background: #fef2f2;
      border: 1px solid #fecaca;
      color: #dc2626;
      padding: 8px 12px;
      border-radius: 6px;
      font-size: 12.5px;
    }
    .modal-footer {
      display: flex;
      justify-content: flex-end;
      gap: 10px;
      padding: 14px 20px;
      border-top: 1px solid #e2e8f0;
      background: #f8fafc;
    }
    .secondary-btn {
      padding: 8px 14px;
      font-size: 13px;
      border: 1px solid #cbd5e1;
      background: #fff;
      color: #334155;
      border-radius: 8px;
      cursor: pointer;
      font-weight: 600;
    }

    @media (max-width: 950px) {
      .charts-row { flex-direction: column; }
      .page-header { flex-direction: column; align-items: flex-start; }
      .filter-header { flex-direction: column; align-items: flex-start; }
    }
  `]
})
export class ReportsComponent implements OnInit {
  private readonly service = inject(ReportsService);
  private readonly dispositionService = inject(DispositionService);
  private readonly auth = inject(AuthService);
  private readonly cdr = inject(ChangeDetectorRef);

  activeTab: 'analytics' | 'config' = 'analytics';

  // Analytics state
  analytics: ComprehensiveAnalyticsReport | null = null;
  selectedPreset = 'today';
  customFromDate = '';
  customToDate = '';
  loading = true;
  exporting = false;
  error = false;

  // Config state
  allDispositions: CallDisposition[] = [];
  includeInactiveDispositions = true;
  loadingConfig = false;
  configActionMessage = '';
  configErrorMessage = '';

  // Modal state
  editModalOpen = false;
  isEditing = false;
  submittingForm = false;
  modalErrorMessage = '';
  editingDispositionId: string | null = null;

  editFormCode = '';
  editFormName = '';
  editFormDescription = '';
  editFormSortOrder = 0;
  editFormRequiresFollowUp = false;
  editFormRequiresNotes = false;
  editFormIsActive = true;

  get maxTrendTotal(): number {
    if (!this.analytics?.charts.callsOverTime.length) return 1;
    return Math.max(1, ...this.analytics.charts.callsOverTime.map(p => p.total));
  }

  canManageDispositions(): boolean {
    const role = this.auth.userRole();
    return role === 'Admin' || role === 'Supervisor';
  }

  ngOnInit(): void {
    const todayStr = new Date().toISOString().split('T')[0];
    this.customFromDate = todayStr;
    this.customToDate = todayStr;
    this.loadAnalytics();
  }

  setActiveTab(tab: 'analytics' | 'config'): void {
    this.activeTab = tab;
    if (tab === 'config' && this.allDispositions.length === 0) {
      this.loadConfig();
    }
  }

  refreshCurrentTab(): void {
    if (this.activeTab === 'analytics') {
      this.loadAnalytics();
    } else {
      this.loadConfig();
    }
  }

  selectPreset(preset: string): void {
    this.selectedPreset = preset;
    if (preset !== 'custom') {
      this.loadAnalytics();
    }
  }

  onCustomDateChange(): void {
    if (this.customFromDate && this.customToDate) {
      this.loadAnalytics();
    }
  }

  loadAnalytics(): void {
    this.loading = true;
    this.error = false;
    this.cdr.markForCheck();

    let fromUtc: string | undefined;
    let toUtc: string | undefined;

    if (this.selectedPreset === 'custom' && this.customFromDate && this.customToDate) {
      const fromObj = new Date(this.customFromDate);
      fromObj.setHours(0, 0, 0, 0);
      fromUtc = fromObj.toISOString();

      const toObj = new Date(this.customToDate);
      toObj.setHours(23, 59, 59, 999);
      toUtc = toObj.toISOString();
    }

    this.service.getAnalytics(this.selectedPreset, fromUtc, toUtc)
      .pipe(
        finalize(() => {
          this.loading = false;
          this.cdr.markForCheck();
        })
      )
      .subscribe({
        next: data => {
          this.analytics = data;
          this.cdr.markForCheck();
        },
        error: () => {
          this.error = true;
          this.cdr.markForCheck();
        }
      });
  }

  exportCsv(): void {
    this.exporting = true;
    this.cdr.markForCheck();

    let fromUtc: string | undefined;
    let toUtc: string | undefined;

    if (this.selectedPreset === 'custom' && this.customFromDate && this.customToDate) {
      const fromObj = new Date(this.customFromDate);
      fromObj.setHours(0, 0, 0, 0);
      fromUtc = fromObj.toISOString();

      const toObj = new Date(this.customToDate);
      toObj.setHours(23, 59, 59, 999);
      toUtc = toObj.toISOString();
    }

    this.service.downloadExportCsv(this.selectedPreset, fromUtc, toUtc)
      .pipe(
        finalize(() => {
          this.exporting = false;
          this.cdr.markForCheck();
        })
      )
      .subscribe({
        next: blob => {
          const url = window.URL.createObjectURL(blob);
          const a = document.createElement('a');
          a.href = url;
          a.download = `call-analytics-${this.selectedPreset}-${new Date().toISOString().split('T')[0]}.csv`;
          document.body.appendChild(a);
          a.click();
          document.body.removeChild(a);
          window.URL.revokeObjectURL(url);
        },
        error: () => {
          alert('Failed to export CSV report. Please retry.');
        }
      });
  }

  getCompletionRate(): number {
    if (!this.analytics || this.analytics.volume.totalCalls === 0) return 0;
    return Math.round((this.analytics.volume.completed / this.analytics.volume.totalCalls) * 100);
  }

  getBarHeight(value: number, max: number): number {
    if (max <= 0) return 0;
    return Math.min(100, Math.round((value / max) * 100));
  }

  getInboundDashArray(): string {
    if (!this.analytics || this.analytics.volume.totalCalls === 0) {
      return '0 251.2';
    }
    const circum = 2 * Math.PI * 40; // ~251.2
    const pct = this.analytics.volume.incoming / this.analytics.volume.totalCalls;
    const strokeLen = pct * circum;
    return `${strokeLen} ${circum}`;
  }

  getDirPct(dir: 'inbound' | 'outbound'): number {
    if (!this.analytics || this.analytics.volume.totalCalls === 0) return 0;
    const count = dir === 'inbound' ? this.analytics.volume.incoming : this.analytics.volume.outgoing;
    return Math.round((count / this.analytics.volume.totalCalls) * 100);
  }

  loadConfig(): void {
    this.loadingConfig = true;
    this.configErrorMessage = '';
    this.cdr.markForCheck();

    this.dispositionService.getDispositions(this.includeInactiveDispositions)
      .pipe(
        finalize(() => {
          this.loadingConfig = false;
          this.cdr.markForCheck();
        })
      )
      .subscribe({
        next: list => {
          this.allDispositions = (list || []).sort((a, b) => a.sortOrder - b.sortOrder);
          this.cdr.markForCheck();
        },
        error: err => {
          this.configErrorMessage = err?.error?.message || 'Failed to load dispositions configuration.';
          this.cdr.markForCheck();
        }
      });
  }

  toggleIncludeInactive(): void {
    this.includeInactiveDispositions = !this.includeInactiveDispositions;
    this.loadConfig();
  }

  openCreateModal(): void {
    this.isEditing = false;
    this.editingDispositionId = null;
    this.editFormCode = '';
    this.editFormName = '';
    this.editFormDescription = '';
    this.editFormSortOrder = (this.allDispositions.length + 1) * 10;
    this.editFormRequiresFollowUp = false;
    this.editFormRequiresNotes = false;
    this.editFormIsActive = true;
    this.modalErrorMessage = '';
    this.editModalOpen = true;
    this.cdr.markForCheck();
  }

  openEditModal(disp: CallDisposition): void {
    this.isEditing = true;
    this.editingDispositionId = disp.id;
    this.editFormCode = disp.code;
    this.editFormName = disp.name;
    this.editFormDescription = disp.description || '';
    this.editFormSortOrder = disp.sortOrder;
    this.editFormRequiresFollowUp = disp.requiresFollowUp;
    this.editFormRequiresNotes = disp.requiresNotes;
    this.editFormIsActive = disp.isActive;
    this.modalErrorMessage = '';
    this.editModalOpen = true;
    this.cdr.markForCheck();
  }

  closeEditModal(): void {
    if (this.submittingForm) return;
    this.editModalOpen = false;
    this.cdr.markForCheck();
  }

  saveDisposition(): void {
    if (!this.editFormName.trim()) return;

    this.submittingForm = true;
    this.modalErrorMessage = '';
    this.cdr.markForCheck();

    if (this.isEditing && this.editingDispositionId) {
      const updateReq: UpdateDispositionRequest = {
        name: this.editFormName.trim(),
        description: this.editFormDescription.trim() || null,
        requiresFollowUp: this.editFormRequiresFollowUp,
        requiresNotes: this.editFormRequiresNotes,
        sortOrder: this.editFormSortOrder,
        isActive: this.editFormIsActive
      };

      this.dispositionService.update(this.editingDispositionId, updateReq)
        .pipe(
          finalize(() => {
            this.submittingForm = false;
            this.cdr.markForCheck();
          })
        )
        .subscribe({
          next: () => {
            this.editModalOpen = false;
            this.configActionMessage = `Disposition "${this.editFormName}" updated successfully.`;
            this.loadConfig();
          },
          error: err => {
            this.modalErrorMessage = err?.error?.message || 'Failed to update disposition.';
            this.cdr.markForCheck();
          }
        });
    } else {
      const createReq: CreateDispositionRequest = {
        code: this.editFormCode.trim().toUpperCase(),
        name: this.editFormName.trim(),
        description: this.editFormDescription.trim() || null,
        requiresFollowUp: this.editFormRequiresFollowUp,
        requiresNotes: this.editFormRequiresNotes,
        sortOrder: this.editFormSortOrder
      };

      this.dispositionService.create(createReq)
        .pipe(
          finalize(() => {
            this.submittingForm = false;
            this.cdr.markForCheck();
          })
        )
        .subscribe({
          next: () => {
            this.editModalOpen = false;
            this.configActionMessage = `Disposition "${createReq.name}" created successfully.`;
            this.loadConfig();
          },
          error: err => {
            this.modalErrorMessage = err?.error?.message || 'Failed to create disposition.';
            this.cdr.markForCheck();
          }
        });
    }
  }

  toggleDispositionStatus(disp: CallDisposition): void {
    this.dispositionService.toggleStatus(disp.id)
      .subscribe({
        next: updated => {
          this.configActionMessage = `Disposition "${disp.name}" is now ${updated.isActive ? 'Active' : 'Inactive'}.`;
          this.loadConfig();
        },
        error: err => {
          this.configErrorMessage = err?.error?.message || 'Failed to toggle disposition status.';
          this.cdr.markForCheck();
        }
      });
  }

  getOutcomeIcon(code: string): string {
    switch (code?.toUpperCase()) {
      case 'ORDER_COMPLETED': return '🛒';
      case 'CUSTOMER_INTERESTED': return '⭐';
      case 'FOLLOW_UP_REQUIRED': return '⏰';
      case 'INFO_PROVIDED': return 'ℹ️';
      case 'COMPLAINT': return '⚠️';
      case 'NO_RESOLUTION': return '🔄';
      case 'WRONG_NUMBER': return '❌';
      case 'OTHER': return '📝';
      default: return '📋';
    }
  }

  formatDuration(s?: number | null): string {
    if (s == null) return '00:00';
    const m = Math.floor(s / 60);
    const sec = Math.round(s % 60);
    return `${String(m).padStart(2, '0')}:${String(sec).padStart(2, '0')}`;
  }

  formatLongDuration(s?: number | null): string {
    if (s == null || s === 0) return '0s';
    const h = Math.floor(s / 3600);
    const m = Math.floor((s % 3600) / 60);
    const sec = Math.round(s % 60);
    if (h > 0) {
      return `${h}h ${m}m`;
    }
    if (m > 0) {
      return `${m}m ${sec}s`;
    }
    return `${sec}s`;
  }
}
