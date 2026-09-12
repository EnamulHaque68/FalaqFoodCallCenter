import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  OnInit,
  inject
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { AuditLogItem, AuditLogPagedResult, AuditLogFilter } from '../../core/models/audit.models';
import { AuditService } from '../../core/services/audit.service';

@Component({
  selector: 'app-audit-logs',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="audit-page">
      <!-- Header -->
      <div class="page-header">
        <div>
          <span class="eyebrow">Governance & Security</span>
          <h2>System Audit Logs</h2>
          <p>Review comprehensive historical audit trails for logins, telephony routing, customer, agent, settings, and user changes.</p>
        </div>
        <div class="header-actions">
          <button type="button" class="button secondary" [disabled]="loading" (click)="loadLogs()">
            <span [class.spinning]="loading">↻</span> Refresh
          </button>
        </div>
      </div>

      <!-- Feedback Banners -->
      @if (errorMessage) {
        <div class="error-banner">
          <span>⚠️ {{ errorMessage }}</span>
          <button type="button" class="close-btn" (click)="errorMessage = ''">✕</button>
        </div>
      }

      <!-- Metric Summary Cards -->
      <div class="metrics-row">
        <div class="metric-card">
          <span class="metric-label">Total Log Records</span>
          <strong class="metric-value">{{ totalCount }}</strong>
          <span class="metric-sub">Matching current filters</span>
        </div>
        <div class="metric-card">
          <span class="metric-label">Page Count</span>
          <strong class="metric-value text-blue">{{ logs.length }}</strong>
          <span class="metric-sub">Logs on page {{ currentPage }} of {{ totalPages || 1 }}</span>
        </div>
        <div class="metric-card">
          <span class="metric-label">Actions Tracked</span>
          <strong class="metric-value text-purple">{{ actionsList.length }}</strong>
          <span class="metric-sub">Distinct system action types</span>
        </div>
        <div class="metric-card">
          <span class="metric-label">Monitored Entities</span>
          <strong class="metric-value text-success">{{ entitiesList.length }}</strong>
          <span class="metric-sub">Domain entity schemas</span>
        </div>
      </div>

      <!-- Filters Toolbar -->
      <div class="filters-bar">
        <div class="search-input-wrap">
          <span class="search-icon">🔍</span>
          <input
            type="text"
            placeholder="Search action, actor, entity ID or details…"
            [formControl]="searchControl"
            (keyup.enter)="applyFilters()"
          />
        </div>

        <div class="filter-group">
          <select [formControl]="actionFilterControl" (change)="applyFilters()">
            <option value="">All Actions</option>
            @for (action of actionsList; track action) {
              <option [value]="action">{{ action }}</option>
            }
          </select>

          <select [formControl]="entityFilterControl" (change)="applyFilters()">
            <option value="">All Entities</option>
            @for (entity of entitiesList; track entity) {
              <option [value]="entity">{{ entity }}</option>
            }
          </select>

          <div class="date-range-group">
            <label class="date-label">From:</label>
            <input type="date" [formControl]="fromDateControl" (change)="applyFilters()" />
            <label class="date-label">To:</label>
            <input type="date" [formControl]="toDateControl" (change)="applyFilters()" />
          </div>

          <select [formControl]="pageSizeControl" (change)="onPageSizeChange()">
            <option [value]="10">10 / page</option>
            <option [value]="20">20 / page</option>
            <option [value]="50">50 / page</option>
            <option [value]="100">100 / page</option>
          </select>

          @if (hasActiveFilters()) {
            <button type="button" class="clear-filters-btn" (click)="resetFilters()">
              Clear Filters
            </button>
          }
        </div>
      </div>

      <!-- Table Container -->
      <div class="table-card">
        @if (loading && logs.length === 0) {
          <div class="loading-state">
            <div class="spinner"></div>
            <p>Loading audit trail records…</p>
          </div>
        } @else if (logs.length === 0) {
          <div class="empty-state">
            <span class="empty-icon">📜</span>
            <h3>No audit records found</h3>
            <p>No logged events match the specified filter criteria. Try broadening your search or resetting date filters.</p>
            @if (hasActiveFilters()) {
              <button type="button" class="button secondary" (click)="resetFilters()">
                Reset Filters
              </button>
            }
          </div>
        } @else {
          <table class="audit-table">
            <thead>
              <tr>
                <th style="width: 180px;">Timestamp</th>
                <th style="width: 160px;">Action</th>
                <th style="width: 160px;">Entity</th>
                <th style="width: 200px;">Actor</th>
                <th>Metadata Preview</th>
                <th style="width: 100px; text-align: right;">Details</th>
              </tr>
            </thead>
            <tbody>
              @for (log of logs; track log.id) {
                <tr>
                  <td>
                    <div class="timestamp-cell">
                      <span class="time-main">{{ log.createdAt | date:'mediumDate' }}</span>
                      <span class="time-sub">{{ log.createdAt | date:'HH:mm:ss' }} UTC</span>
                    </div>
                  </td>
                  <td>
                    <span class="badge badge-action" [ngClass]="getActionBadgeClass(log.action)">
                      {{ log.action }}
                    </span>
                  </td>
                  <td>
                    <div class="entity-cell">
                      <span class="entity-name">{{ log.entityName }}</span>
                      @if (log.entityId) {
                        <span class="entity-id" title="{{ log.entityId }}">{{ truncate(log.entityId, 16) }}</span>
                      }
                    </div>
                  </td>
                  <td>
                    <div class="actor-cell">
                      @if (log.userName) {
                        <span class="actor-name">👤 {{ log.userName }}</span>
                        @if (log.userRole) {
                          <span class="role-pill" [ngClass]="getRoleClass(log.userRole)">{{ log.userRole }}</span>
                        }
                      } @else {
                        <span class="actor-system">⚙️ System / Automated</span>
                      }
                    </div>
                  </td>
                  <td>
                    <div class="details-preview" title="{{ log.detailsJson }}">
                      {{ formatDetailsPreview(log.detailsJson) }}
                    </div>
                  </td>
                  <td style="text-align: right;">
                    <button
                      type="button"
                      class="btn-view-details"
                      title="Inspect full audit record"
                      (click)="openDetailsModal(log)">
                      Inspect
                    </button>
                  </td>
                </tr>
              }
            </tbody>
          </table>

          <!-- Pagination Bar -->
          <div class="pagination-bar">
            <span class="pagination-info">
              Showing {{ getStartIndex() }}–{{ getEndIndex() }} of {{ totalCount }} audit events
            </span>
            <div class="pagination-actions">
              <button
                type="button"
                class="btn-page"
                [disabled]="currentPage <= 1 || loading"
                (click)="goToPage(currentPage - 1)">
                ← Previous
              </button>
              <span class="page-current">Page {{ currentPage }} of {{ totalPages || 1 }}</span>
              <button
                type="button"
                class="btn-page"
                [disabled]="currentPage >= totalPages || loading"
                (click)="goToPage(currentPage + 1)">
                Next →
              </button>
            </div>
          </div>
        }
      </div>

      <!-- Modal: Audit Details Inspection -->
      @if (selectedLog) {
        <div class="modal-backdrop" (click)="closeDetailsModal()">
          <div class="modal-dialog" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <div>
                <span class="eyebrow">Audit Entry Details</span>
                <h3>{{ selectedLog.action }} on {{ selectedLog.entityName }}</h3>
              </div>
              <button type="button" class="close-btn" (click)="closeDetailsModal()">✕</button>
            </div>

            <div class="modal-body">
              <div class="detail-grid">
                <div class="detail-item">
                  <span class="detail-label">Audit Record ID</span>
                  <span class="detail-val mono">{{ selectedLog.id }}</span>
                </div>
                <div class="detail-item">
                  <span class="detail-label">Timestamp (UTC)</span>
                  <span class="detail-val">{{ selectedLog.createdAt | date:'yyyy-MM-dd HH:mm:ss' }} UTC</span>
                </div>
                <div class="detail-item">
                  <span class="detail-label">Actor User</span>
                  <span class="detail-val">
                    {{ selectedLog.userName || 'System / Service Engine' }}
                    @if (selectedLog.userRole) {
                      ({{ selectedLog.userRole }})
                    }
                  </span>
                </div>
                <div class="detail-item">
                  <span class="detail-label">Actor User ID</span>
                  <span class="detail-val mono">{{ selectedLog.userId || 'N/A' }}</span>
                </div>
                <div class="detail-item">
                  <span class="detail-label">Target Entity</span>
                  <span class="detail-val">{{ selectedLog.entityName }}</span>
                </div>
                <div class="detail-item">
                  <span class="detail-label">Target Entity ID</span>
                  <span class="detail-val mono">{{ selectedLog.entityId || 'N/A' }}</span>
                </div>
              </div>

              <div class="payload-section">
                <div class="payload-header">
                  <h4>Audit Metadata & Payload (JSON)</h4>
                  <button type="button" class="btn-copy" (click)="copyPayload()">
                    {{ copySuccess ? '✓ Copied' : '📋 Copy JSON' }}
                  </button>
                </div>
                @if (selectedLog.detailsJson) {
                  <pre class="json-code"><code>{{ formatJson(selectedLog.detailsJson) }}</code></pre>
                } @else {
                  <p class="no-payload">No additional payload recorded for this event.</p>
                }
              </div>
            </div>

            <div class="modal-footer">
              <button type="button" class="button secondary" (click)="closeDetailsModal()">
                Close
              </button>
            </div>
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    .audit-page {
      display: flex;
      flex-direction: column;
      gap: 20px;
      padding-bottom: 40px;
    }

    .page-header {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      gap: 16px;
      flex-wrap: wrap;
    }

    .eyebrow {
      font-size: 11px;
      font-weight: 700;
      color: #6366f1;
      text-transform: uppercase;
      letter-spacing: 0.08em;
    }

    .page-header h2 {
      margin: 2px 0 6px;
      font-size: 24px;
      font-weight: 800;
      color: #0f172a;
    }

    .page-header p {
      margin: 0;
      font-size: 13px;
      color: #64748b;
      max-width: 680px;
    }

    .header-actions {
      display: flex;
      gap: 10px;
    }

    .button {
      border: 0;
      border-radius: 8px;
      padding: 9px 16px;
      font-weight: 600;
      font-size: 13px;
      cursor: pointer;
      display: inline-flex;
      align-items: center;
      gap: 6px;
      transition: background 0.15s ease, transform 0.05s ease;
    }

    .button:active {
      transform: scale(0.98);
    }

    .button.secondary {
      background: #e2e8f0;
      color: #1e293b;
    }
    .button.secondary:hover {
      background: #cbd5e1;
    }

    .spinning {
      display: inline-block;
      animation: spin 1s infinite linear;
    }
    @keyframes spin {
      from { transform: rotate(0deg); }
      to { transform: rotate(360deg); }
    }

    /* Feedback */
    .error-banner {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 12px 16px;
      border-radius: 8px;
      font-size: 13px;
      font-weight: 500;
      background: #fef2f2;
      border: 1px solid #fecaca;
      color: #991b1b;
    }

    .close-btn {
      background: none;
      border: none;
      font-size: 16px;
      cursor: pointer;
      color: inherit;
      padding: 0 4px;
    }

    /* Metrics */
    .metrics-row {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
      gap: 14px;
    }

    .metric-card {
      background: #fff;
      border: 1px solid #e2e8f0;
      border-radius: 10px;
      padding: 16px;
      display: flex;
      flex-direction: column;
      gap: 4px;
      box-shadow: 0 1px 3px rgba(0,0,0,0.02);
    }

    .metric-label {
      font-size: 11px;
      font-weight: 700;
      color: #64748b;
      text-transform: uppercase;
      letter-spacing: 0.04em;
    }

    .metric-value {
      font-size: 24px;
      font-weight: 800;
      color: #0f172a;
    }

    .metric-sub {
      font-size: 12px;
      color: #94a3b8;
    }

    .text-success { color: #10b981 !important; }
    .text-purple { color: #8b5cf6 !important; }
    .text-blue { color: #3b82f6 !important; }

    /* Filters Bar */
    .filters-bar {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 12px;
      flex-wrap: wrap;
      background: #fff;
      padding: 12px 16px;
      border: 1px solid #e2e8f0;
      border-radius: 10px;
    }

    .search-input-wrap {
      display: flex;
      align-items: center;
      gap: 8px;
      background: #f8fafc;
      border: 1px solid #cbd5e1;
      border-radius: 8px;
      padding: 6px 12px;
      min-width: 280px;
      flex: 1;
    }

    .search-input-wrap input {
      border: none;
      background: transparent;
      outline: none;
      font-size: 13px;
      width: 100%;
    }

    .filter-group {
      display: flex;
      align-items: center;
      gap: 8px;
      flex-wrap: wrap;
    }

    .filter-group select,
    .date-range-group input[type="date"] {
      border: 1px solid #cbd5e1;
      border-radius: 8px;
      padding: 6px 10px;
      font-size: 13px;
      background: #fff;
      color: #334155;
      outline: none;
    }

    .date-range-group {
      display: flex;
      align-items: center;
      gap: 6px;
    }

    .date-label {
      font-size: 12px;
      font-weight: 600;
      color: #64748b;
    }

    .clear-filters-btn {
      background: none;
      border: 1px solid #cbd5e1;
      border-radius: 8px;
      padding: 6px 12px;
      font-size: 12px;
      font-weight: 600;
      color: #64748b;
      cursor: pointer;
    }
    .clear-filters-btn:hover {
      background: #f1f5f9;
      color: #0f172a;
    }

    /* Table */
    .table-card {
      background: #fff;
      border: 1px solid #e2e8f0;
      border-radius: 10px;
      overflow: hidden;
      box-shadow: 0 1px 3px rgba(0,0,0,0.02);
    }

    .audit-table {
      width: 100%;
      border-collapse: collapse;
      font-size: 13px;
      text-align: left;
    }

    .audit-table th {
      background: #f8fafc;
      color: #475569;
      font-weight: 700;
      font-size: 12px;
      text-transform: uppercase;
      letter-spacing: 0.04em;
      padding: 12px 16px;
      border-bottom: 1px solid #e2e8f0;
    }

    .audit-table td {
      padding: 12px 16px;
      border-bottom: 1px solid #f1f5f9;
      color: #334155;
      vertical-align: middle;
    }

    .audit-table tr:hover td {
      background: #f8fafc;
    }

    .timestamp-cell {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }
    .time-main {
      font-weight: 600;
      color: #0f172a;
    }
    .time-sub {
      font-size: 11px;
      color: #64748b;
      font-family: monospace;
    }

    .badge {
      display: inline-block;
      padding: 4px 8px;
      border-radius: 6px;
      font-size: 11px;
      font-weight: 700;
      letter-spacing: 0.02em;
    }

    .badge-action {
      background: #f1f5f9;
      color: #475569;
    }
    .badge-login { background: #dcfce7; color: #15803d; }
    .badge-logout { background: #f1f5f9; color: #64748b; }
    .badge-created { background: #dbeafe; color: #1d4ed8; }
    .badge-updated { background: #fef3c7; color: #b45309; }
    .badge-deleted { background: #fee2e2; color: #b91c1c; }
    .badge-call { background: #f3e8ff; color: #7e22ce; }
    .badge-settings { background: #e0e7ff; color: #4338ca; }

    .entity-cell {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }
    .entity-name {
      font-weight: 600;
      color: #1e293b;
    }
    .entity-id {
      font-family: monospace;
      font-size: 11px;
      color: #64748b;
    }

    .actor-cell {
      display: flex;
      align-items: center;
      gap: 6px;
      flex-wrap: wrap;
    }
    .actor-name {
      font-weight: 600;
      color: #0f172a;
    }
    .actor-system {
      font-style: italic;
      color: #64748b;
      font-size: 12px;
    }

    .role-pill {
      font-size: 10px;
      font-weight: 700;
      padding: 2px 6px;
      border-radius: 4px;
      background: #e2e8f0;
      color: #334155;
    }
    .role-admin { background: #f3e8ff; color: #6b21a8; }
    .role-supervisor { background: #e0f2fe; color: #0369a1; }
    .role-agent { background: #ecfdf5; color: #047857; }

    .details-preview {
      font-family: monospace;
      font-size: 12px;
      color: #475569;
      max-width: 320px;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .btn-view-details {
      background: #f1f5f9;
      border: 1px solid #cbd5e1;
      border-radius: 6px;
      padding: 5px 10px;
      font-size: 12px;
      font-weight: 600;
      color: #334155;
      cursor: pointer;
      transition: background 0.15s ease;
    }
    .btn-view-details:hover {
      background: #e2e8f0;
      color: #0f172a;
    }

    /* Pagination */
    .pagination-bar {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 12px 16px;
      border-top: 1px solid #e2e8f0;
      background: #f8fafc;
    }

    .pagination-info {
      font-size: 12px;
      color: #64748b;
    }

    .pagination-actions {
      display: flex;
      align-items: center;
      gap: 10px;
    }

    .btn-page {
      background: #fff;
      border: 1px solid #cbd5e1;
      border-radius: 6px;
      padding: 6px 12px;
      font-size: 12px;
      font-weight: 600;
      color: #334155;
      cursor: pointer;
    }
    .btn-page:disabled {
      opacity: 0.5;
      cursor: not-allowed;
    }
    .btn-page:not(:disabled):hover {
      background: #f1f5f9;
    }

    .page-current {
      font-size: 12px;
      font-weight: 600;
      color: #0f172a;
    }

    /* Empty & Loading States */
    .loading-state, .empty-state {
      padding: 48px 16px;
      text-align: center;
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 10px;
      color: #64748b;
    }

    .empty-icon { font-size: 36px; }
    .empty-state h3 { margin: 0; font-size: 16px; color: #0f172a; }
    .empty-state p { margin: 0 0 12px; max-width: 420px; font-size: 13px; }

    .spinner {
      width: 28px;
      height: 28px;
      border: 3px solid #e2e8f0;
      border-top-color: #6366f1;
      border-radius: 50%;
      animation: spin 0.8s infinite linear;
    }

    /* Modal Details Dialog */
    .modal-backdrop {
      position: fixed;
      inset: 0;
      background: rgba(15, 23, 42, 0.5);
      backdrop-filter: blur(2px);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1000;
      padding: 16px;
    }

    .modal-dialog {
      background: #fff;
      border-radius: 12px;
      width: 100%;
      max-width: 680px;
      box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.1), 0 10px 10px -5px rgba(0, 0, 0, 0.04);
      display: flex;
      flex-direction: column;
      max-height: 90vh;
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
      margin: 2px 0 0;
      font-size: 18px;
      color: #0f172a;
    }

    .modal-body {
      padding: 20px;
      overflow-y: auto;
      display: flex;
      flex-direction: column;
      gap: 18px;
    }

    .detail-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 12px;
      background: #f8fafc;
      padding: 14px;
      border-radius: 8px;
      border: 1px solid #e2e8f0;
    }

    .detail-item {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }
    .detail-label {
      font-size: 11px;
      font-weight: 700;
      color: #64748b;
      text-transform: uppercase;
      letter-spacing: 0.04em;
    }
    .detail-val {
      font-size: 13px;
      color: #0f172a;
      font-weight: 500;
    }
    .detail-val.mono {
      font-family: monospace;
      font-size: 12px;
    }

    .payload-section {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .payload-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
    }
    .payload-header h4 {
      margin: 0;
      font-size: 13px;
      font-weight: 700;
      color: #1e293b;
    }

    .btn-copy {
      background: #f1f5f9;
      border: 1px solid #cbd5e1;
      border-radius: 6px;
      padding: 4px 8px;
      font-size: 11px;
      font-weight: 600;
      color: #334155;
      cursor: pointer;
    }
    .btn-copy:hover {
      background: #e2e8f0;
    }

    .json-code {
      background: #0f172a;
      color: #f8fafc;
      padding: 14px;
      border-radius: 8px;
      font-family: 'Consolas', 'Courier New', monospace;
      font-size: 12px;
      line-height: 1.5;
      margin: 0;
      max-height: 300px;
      overflow: auto;
    }

    .no-payload {
      margin: 0;
      font-size: 12px;
      color: #94a3b8;
      font-style: italic;
    }

    .modal-footer {
      display: flex;
      justify-content: flex-end;
      padding: 14px 20px;
      border-top: 1px solid #e2e8f0;
      background: #f8fafc;
    }
  `]
})
export class AuditLogsComponent implements OnInit {
  private readonly auditService = inject(AuditService);
  private readonly cdr = inject(ChangeDetectorRef);

  logs: AuditLogItem[] = [];
  actionsList: string[] = [];
  entitiesList: string[] = [];

  currentPage = 1;
  pageSize = 20;
  totalCount = 0;
  totalPages = 0;

  loading = false;
  errorMessage = '';
  copySuccess = false;

  selectedLog: AuditLogItem | null = null;

  searchControl = new FormControl('');
  actionFilterControl = new FormControl('');
  entityFilterControl = new FormControl('');
  fromDateControl = new FormControl('');
  toDateControl = new FormControl('');
  pageSizeControl = new FormControl(20);

  ngOnInit(): void {
    this.loadFilterLookups();
    this.loadLogs();
  }

  loadFilterLookups(): void {
    this.auditService.getActions().subscribe({
      next: actions => {
        this.actionsList = actions;
        this.cdr.markForCheck();
      }
    });

    this.auditService.getEntities().subscribe({
      next: entities => {
        this.entitiesList = entities;
        this.cdr.markForCheck();
      }
    });
  }

  loadLogs(): void {
    this.loading = true;
    this.errorMessage = '';
    this.cdr.markForCheck();

    const filter: AuditLogFilter = {
      page: this.currentPage,
      pageSize: this.pageSize,
      search: this.searchControl.value || undefined,
      action: this.actionFilterControl.value || undefined,
      entityName: this.entityFilterControl.value || undefined,
      fromDate: this.fromDateControl.value || undefined,
      toDate: this.toDateControl.value || undefined
    };

    this.auditService.getPaged(filter)
      .pipe(finalize(() => {
        this.loading = false;
        this.cdr.markForCheck();
      }))
      .subscribe({
        next: result => {
          this.logs = result.items;
          this.currentPage = result.page;
          this.pageSize = result.pageSize;
          this.totalCount = result.totalCount;
          this.totalPages = result.totalPages;
        },
        error: err => {
          this.errorMessage = err.error?.message || 'Failed to load audit logs. Please check your network or credentials.';
        }
      });
  }

  applyFilters(): void {
    this.currentPage = 1;
    this.loadLogs();
  }

  onPageSizeChange(): void {
    this.pageSize = Number(this.pageSizeControl.value) || 20;
    this.currentPage = 1;
    this.loadLogs();
  }

  resetFilters(): void {
    this.searchControl.setValue('');
    this.actionFilterControl.setValue('');
    this.entityFilterControl.setValue('');
    this.fromDateControl.setValue('');
    this.toDateControl.setValue('');
    this.pageSizeControl.setValue(20);
    this.pageSize = 20;
    this.currentPage = 1;
    this.loadLogs();
  }

  hasActiveFilters(): boolean {
    return Boolean(
      this.searchControl.value?.trim() ||
      this.actionFilterControl.value ||
      this.entityFilterControl.value ||
      this.fromDateControl.value ||
      this.toDateControl.value ||
      this.pageSize !== 20
    );
  }

  goToPage(page: number): void {
    if (page < 1 || page > this.totalPages) return;
    this.currentPage = page;
    this.loadLogs();
  }

  getStartIndex(): number {
    if (this.totalCount === 0) return 0;
    return (this.currentPage - 1) * this.pageSize + 1;
  }

  getEndIndex(): number {
    return Math.min(this.currentPage * this.pageSize, this.totalCount);
  }

  truncate(val: string, maxLen: number): string {
    if (!val || val.length <= maxLen) return val;
    return `${val.substring(0, maxLen)}…`;
  }

  formatDetailsPreview(detailsJson?: string | null): string {
    if (!detailsJson) return '—';
    try {
      const obj = JSON.parse(detailsJson);
      const keys = Object.keys(obj);
      if (keys.length === 0) return '{}';
      return keys.map(k => `${k}: ${JSON.stringify(obj[k])}`).join(', ');
    } catch {
      return detailsJson;
    }
  }

  formatJson(detailsJson?: string | null): string {
    if (!detailsJson) return '';
    try {
      const obj = JSON.parse(detailsJson);
      return JSON.stringify(obj, null, 2);
    } catch {
      return detailsJson;
    }
  }

  getActionBadgeClass(action: string): string {
    const act = (action || '').toLowerCase();
    if (act.includes('login')) return 'badge-login';
    if (act.includes('logout')) return 'badge-logout';
    if (act.includes('create')) return 'badge-created';
    if (act.includes('update') || act.includes('change') || act.includes('reset')) return 'badge-updated';
    if (act.includes('delete') || act.includes('deactivate')) return 'badge-deleted';
    if (act.includes('call') || act.includes('transfer') || act.includes('assign')) return 'badge-call';
    if (act.includes('setting')) return 'badge-settings';
    return 'badge-action';
  }

  getRoleClass(role: string): string {
    const r = (role || '').toLowerCase();
    if (r === 'admin') return 'role-admin';
    if (r === 'supervisor') return 'role-supervisor';
    return 'role-agent';
  }

  openDetailsModal(log: AuditLogItem): void {
    this.selectedLog = log;
    this.copySuccess = false;
    this.cdr.markForCheck();
  }

  closeDetailsModal(): void {
    this.selectedLog = null;
    this.copySuccess = false;
    this.cdr.markForCheck();
  }

  copyPayload(): void {
    if (!this.selectedLog?.detailsJson) return;
    navigator.clipboard.writeText(this.formatJson(this.selectedLog.detailsJson)).then(() => {
      this.copySuccess = true;
      this.cdr.markForCheck();
      setTimeout(() => {
        this.copySuccess = false;
        this.cdr.markForCheck();
      }, 2000);
    });
  }
}
