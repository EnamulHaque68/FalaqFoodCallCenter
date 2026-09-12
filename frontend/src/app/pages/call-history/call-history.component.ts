import { ChangeDetectionStrategy, ChangeDetectorRef, Component, DestroyRef, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { debounceTime, distinctUntilChanged, finalize } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CallHistoryService } from '../../core/services/call-history.service';
import { AgentService } from '../../core/services/agent.service';
import { DispositionService } from '../../core/services/disposition.service';
import { AgentResponse } from '../../core/models/agent.models';
import { CallDisposition } from '../../core/models/disposition.models';
import { CallDirection, CallStatus } from '../../core/models/agent-dashboard.models';
import { CallHistoryItem, CallHistoryPagedResult } from '../../core/models/call-history.models';

@Component({
  selector: 'app-call-history',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page-header">
      <div>
        <span class="eyebrow">Operations & Audit</span>
        <h2>Call History & Records</h2>
        <p>Comprehensive auditable call records, search, lifecycle filters, and detailed session inspection.</p>
      </div>
      <div class="header-actions">
        <button class="button secondary" type="button" [disabled]="loading" (click)="resetFilters()">
          Clear Filters
        </button>
        <button class="button primary" type="button" [disabled]="loading" (click)="loadPage(1)">
          Refresh
        </button>
      </div>
    </div>

    <!-- Filters Section -->
    <section class="filter-card">
      <div class="filter-grid">
        <label class="field-search">
          <span>Search</span>
          <div class="input-with-icon">
            <span class="icon">🔍</span>
            <input [formControl]="search" type="search" placeholder="Customer name, phone, or agent...">
          </div>
        </label>

        <label>
          <span>From Date</span>
          <input type="date" [formControl]="filters.controls.fromDate">
        </label>

        <label>
          <span>To Date</span>
          <input type="date" [formControl]="filters.controls.toDate">
        </label>

        <label>
          <span>Direction</span>
          <select [formControl]="filters.controls.direction">
            <option [ngValue]="null">All Directions</option>
            <option [ngValue]="CallDirection.Inbound">📥 Inbound</option>
            <option [ngValue]="CallDirection.Outbound">📤 Outbound</option>
          </select>
        </label>

        <label>
          <span>Status</span>
          <select [formControl]="filters.controls.status">
            <option [ngValue]="null">All Statuses</option>
            @for (st of statuses; track st.value) {
              <option [ngValue]="st.value">{{st.label}}</option>
            }
          </select>
        </label>

        <label>
          <span>Agent</span>
          <select [formControl]="filters.controls.agentId">
            <option value="">All Agents</option>
            @for (agent of agents; track agent.id) {
              <option [value]="agent.id">{{agent.displayName}}</option>
            }
          </select>
        </label>

        <label>
          <span>Disposition</span>
          <select [formControl]="filters.controls.dispositionId">
            <option value="">All Outcomes</option>
            @for (disp of dispositions; track disp.id) {
              <option [value]="disp.id">{{disp.name}}</option>
            }
          </select>
        </label>

        <label>
          <span>Page Size</span>
          <select [formControl]="filters.controls.pageSize">
            <option [ngValue]="10">10 per page</option>
            <option [ngValue]="20">20 per page</option>
            <option [ngValue]="50">50 per page</option>
            <option [ngValue]="100">100 per page</option>
          </select>
        </label>
      </div>
    </section>

    @if (errorMessage) {
      <div class="error-banner">
        <span>⚠️ {{errorMessage}}</span>
        <button type="button" class="button secondary btn-sm" (click)="loadPage(page)">Retry</button>
      </div>
    }

    <!-- Call Records Content -->
    <section class="content-card">
      <div class="card-toolbar">
        <div class="summary-badge">
          <strong>{{result.totalCount}}</strong> Total Calls Found
        </div>
        <div class="page-indicator">
          Page {{result.page}} of {{result.totalPages || 1}}
        </div>
      </div>

      @if (loading) {
        <div class="table-state">
          <div class="spinner"></div>
          <strong>Loading call history...</strong>
          <span>Querying database with active filters.</span>
        </div>
      } @else if (!result.items.length) {
        <div class="table-state">
          <span class="empty-icon">📋</span>
          <strong>No call records match your criteria</strong>
          <span>Try expanding your date range or adjusting the filter conditions.</span>
        </div>
      } @else {
        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Customer</th>
                <th>Agent</th>
                <th>Direction</th>
                <th>Status</th>
                <th>Started At</th>
                <th>Duration</th>
                <th>Disposition</th>
                <th>Notes</th>
                <th class="text-right">Action</th>
              </tr>
            </thead>
            <tbody>
              @for (call of result.items; track call.id) {
                <tr>
                  <td>
                    <div class="customer-cell">
                      <strong>{{call.customerName || 'Unknown Customer'}}</strong>
                      <span class="phone-tag">📞 {{call.customerPhone || call.phoneNumber}}</span>
                    </div>
                  </td>
                  <td>
                    <div class="agent-cell">
                      @if (call.agentName) {
                        <span class="agent-badge">👤 {{call.agentName}}</span>
                      } @else {
                        <span class="unassigned-badge">Unassigned</span>
                      }
                    </div>
                  </td>
                  <td>
                    <span class="dir-pill" [class.inbound]="call.direction === CallDirection.Inbound" [class.outbound]="call.direction === CallDirection.Outbound">
                      {{call.direction === CallDirection.Inbound ? '📥 Inbound' : '📤 Outbound'}}
                    </span>
                  </td>
                  <td>
                    <span class="status-pill status-{{statusKey(call.status)}}">
                      {{statusLabel(call.status)}}
                    </span>
                  </td>
                  <td>
                    <div class="time-cell">
                      <span class="primary-time">{{call.startedAt | date:'MMM d, y, h:mm:ss a'}}</span>
                      @if (call.endedAt) {
                        <small class="secondary-time">Ended {{call.endedAt | date:'h:mm:ss a'}}</small>
                      }
                    </div>
                  </td>
                  <td>
                    <span class="duration-badge">{{formatDuration(call.durationSeconds)}}</span>
                  </td>
                  <td>
                    @if (call.dispositionName) {
                      <span class="disposition-tag" title="Outcome: {{call.dispositionName}}">
                        🏷️ {{call.dispositionName}}
                      </span>
                    } @else {
                      <span class="text-muted">—</span>
                    }
                  </td>
                  <td>
                    @if (call.notes) {
                      <span class="notes-preview" [title]="call.notes">
                        💬 {{truncate(call.notes, 28)}}
                      </span>
                    } @else {
                      <span class="text-muted">—</span>
                    }
                  </td>
                  <td class="text-right">
                    <a class="btn-inspect" [routerLink]="['/call-history', call.id]">
                      View Details →
                    </a>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>

        <div class="pagination-bar">
          <span class="pagination-summary">
            Showing {{startIndex}} - {{endIndex}} of {{result.totalCount}} calls
          </span>
          <div class="pagination-controls">
            <button
              type="button"
              class="btn-page"
              [disabled]="result.page <= 1 || loading"
              (click)="loadPage(1)"
              title="First Page">
              «
            </button>
            <button
              type="button"
              class="btn-page"
              [disabled]="!result.hasPreviousPage || loading"
              (click)="loadPage(result.page - 1)">
              ‹ Previous
            </button>
            <span class="page-chip">{{result.page}} / {{result.totalPages || 1}}</span>
            <button
              type="button"
              class="btn-page"
              [disabled]="!result.hasNextPage || loading"
              (click)="loadPage(result.page + 1)">
              Next ›
            </button>
            <button
              type="button"
              class="btn-page"
              [disabled]="result.page >= result.totalPages || loading"
              (click)="loadPage(result.totalPages)"
              title="Last Page">
              »
            </button>
          </div>
        </div>
      }
    </section>
  `,
  styles: [`
    :host {
      display: block;
      padding: 0;
    }

    .page-header {
      display: flex;
      justify-content: space-between;
      align-items: flex-end;
      margin-bottom: 20px;
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
      transition: all 0.15s ease;
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

    .button:disabled {
      opacity: 0.55;
      cursor: not-allowed;
    }

    .btn-sm {
      height: 32px;
      padding: 0 12px;
      font-size: 12px;
    }

    /* Filter Card */
    .filter-card {
      background: #ffffff;
      border: 1px solid #e2e8f0;
      border-radius: 16px;
      padding: 20px;
      margin-bottom: 20px;
      box-shadow: 0 1px 3px rgba(0, 0, 0, 0.04);
    }

    .filter-grid {
      display: grid;
      grid-template-columns: minmax(260px, 2fr) repeat(auto-fit, minmax(140px, 1fr));
      gap: 14px;
      align-items: flex-end;
    }

    .filter-grid label {
      display: grid;
      gap: 6px;
      font-size: 12px;
      font-weight: 700;
      color: #475569;
    }

    .filter-grid input,
    .filter-grid select {
      height: 42px;
      border: 1px solid #cbd5e1;
      border-radius: 10px;
      padding: 0 12px;
      background: #f8fafc;
      color: #0f172a;
      font-size: 13px;
      transition: border-color 0.15s;
    }

    .filter-grid input:focus,
    .filter-grid select:focus {
      outline: none;
      border-color: #6366f1;
      background: #ffffff;
      box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.12);
    }

    .input-with-icon {
      position: relative;
      display: flex;
      align-items: center;
    }

    .input-with-icon .icon {
      position: absolute;
      left: 12px;
      font-size: 14px;
      color: #94a3b8;
      pointer-events: none;
    }

    .input-with-icon input {
      padding-left: 36px;
      width: 100%;
    }

    /* Error Banner */
    .error-banner {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 14px 18px;
      background: #fef2f2;
      border: 1px solid #fecaca;
      border-radius: 12px;
      color: #991b1b;
      font-weight: 600;
      font-size: 13px;
      margin-bottom: 20px;
    }

    /* Content Card */
    .content-card {
      background: #ffffff;
      border: 1px solid #e2e8f0;
      border-radius: 16px;
      overflow: hidden;
      box-shadow: 0 1px 3px rgba(0, 0, 0, 0.04);
    }

    .card-toolbar {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 16px 20px;
      background: #f8fafc;
      border-bottom: 1px solid #e2e8f0;
    }

    .summary-badge {
      font-size: 13px;
      color: #475569;
    }

    .summary-badge strong {
      color: #0f172a;
      font-weight: 800;
      font-size: 15px;
    }

    .page-indicator {
      font-size: 12px;
      font-weight: 700;
      color: #64748b;
      background: #e2e8f0;
      padding: 4px 10px;
      border-radius: 999px;
    }

    .table-wrap {
      overflow-x: auto;
    }

    table {
      width: 100%;
      border-collapse: collapse;
      min-width: 1050px;
    }

    th, td {
      padding: 14px 18px;
      text-align: left;
      border-bottom: 1px solid #f1f5f9;
      font-size: 13px;
      vertical-align: middle;
    }

    th {
      font-size: 11px;
      font-weight: 800;
      text-transform: uppercase;
      letter-spacing: 0.06em;
      color: #64748b;
      background: #fafafa;
    }

    tbody tr:hover {
      background: #f8fafc;
    }

    .customer-cell {
      display: grid;
      gap: 2px;
    }

    .customer-cell strong {
      color: #0f172a;
      font-weight: 700;
    }

    .phone-tag {
      font-size: 11px;
      color: #64748b;
      font-family: monospace;
    }

    .agent-badge {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      padding: 4px 9px;
      border-radius: 6px;
      background: #f1f5f9;
      color: #1e293b;
      font-weight: 600;
      font-size: 12px;
    }

    .unassigned-badge {
      font-size: 11px;
      color: #94a3b8;
      font-style: italic;
    }

    .dir-pill {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      padding: 4px 9px;
      border-radius: 999px;
      font-size: 11px;
      font-weight: 700;
    }

    .dir-pill.inbound {
      background: #e0f2fe;
      color: #0369a1;
    }

    .dir-pill.outbound {
      background: #f3e8ff;
      color: #7e22ce;
    }

    .status-pill {
      display: inline-flex;
      align-items: center;
      padding: 4px 10px;
      border-radius: 999px;
      font-size: 11px;
      font-weight: 700;
      background: #f1f5f9;
      color: #475569;
    }

    .status-completed { background: #dcfce7; color: #15803d; }
    .status-connected { background: #e0e7ff; color: #4338ca; }
    .status-onhold { background: #fef3c7; color: #b45309; }
    .status-ringing { background: #ffedd5; color: #c2410c; }
    .status-queued { background: #f1f5f9; color: #64748b; }
    .status-rejected { background: #fee2e2; color: #b91c1c; }
    .status-abandoned { background: #f1f5f9; color: #94a3b8; }

    .time-cell {
      display: grid;
      gap: 2px;
    }

    .primary-time {
      color: #1e293b;
      font-size: 12px;
      font-weight: 600;
    }

    .secondary-time {
      color: #64748b;
      font-size: 11px;
    }

    .duration-badge {
      display: inline-block;
      font-family: monospace;
      font-size: 12px;
      font-weight: 700;
      color: #334155;
      background: #f8fafc;
      padding: 3px 8px;
      border-radius: 6px;
      border: 1px solid #e2e8f0;
    }

    .disposition-tag {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      padding: 4px 9px;
      border-radius: 6px;
      background: #ecfdf5;
      color: #047857;
      font-weight: 700;
      font-size: 11px;
    }

    .notes-preview {
      display: inline-block;
      max-width: 180px;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      color: #475569;
      font-size: 12px;
    }

    .text-muted {
      color: #94a3b8;
    }

    .text-right {
      text-align: right;
    }

    .btn-inspect {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      padding: 6px 12px;
      border-radius: 8px;
      background: #f8fafc;
      border: 1px solid #cbd5e1;
      color: #4f46e5;
      font-size: 12px;
      font-weight: 700;
      text-decoration: none;
      transition: all 0.15s;
    }

    .btn-inspect:hover {
      background: #4f46e5;
      border-color: #4f46e5;
      color: #ffffff;
    }

    /* Pagination */
    .pagination-bar {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 16px 20px;
      border-top: 1px solid #e2e8f0;
      background: #f8fafc;
    }

    .pagination-summary {
      font-size: 13px;
      color: #64748b;
    }

    .pagination-controls {
      display: flex;
      gap: 6px;
      align-items: center;
    }

    .btn-page {
      height: 34px;
      padding: 0 12px;
      border: 1px solid #cbd5e1;
      border-radius: 8px;
      background: #ffffff;
      color: #334155;
      font-size: 12px;
      font-weight: 700;
      cursor: pointer;
      transition: all 0.15s;
    }

    .btn-page:hover:not(:disabled) {
      border-color: #6366f1;
      color: #4f46e5;
    }

    .btn-page:disabled {
      opacity: 0.45;
      cursor: not-allowed;
    }

    .page-chip {
      font-size: 12px;
      font-weight: 700;
      color: #0f172a;
      padding: 0 8px;
    }

    /* States */
    .table-state {
      padding: 60px 20px;
      display: grid;
      place-content: center;
      justify-items: center;
      gap: 10px;
      text-align: center;
    }

    .empty-icon {
      font-size: 36px;
    }

    .table-state strong {
      font-size: 16px;
      color: #0f172a;
    }

    .table-state span {
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

    @media (max-width: 900px) {
      .filter-grid {
        grid-template-columns: 1fr;
      }
      .page-header {
        flex-direction: column;
        align-items: flex-start;
      }
      .pagination-bar {
        flex-direction: column;
        gap: 12px;
        align-items: flex-start;
      }
    }
  `]
})
export class CallHistoryComponent implements OnInit {
  private readonly service = inject(CallHistoryService);
  private readonly agentService = inject(AgentService);
  private readonly dispositionService = inject(DispositionService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly destroyRef = inject(DestroyRef);

  readonly search = new FormControl('', { nonNullable: true });
  readonly filters = new FormGroup({
    fromDate: new FormControl<string>(''),
    toDate: new FormControl<string>(''),
    direction: new FormControl<CallDirection | null>(null),
    status: new FormControl<CallStatus | null>(null),
    agentId: new FormControl<string>('', { nonNullable: true }),
    dispositionId: new FormControl<string>('', { nonNullable: true }),
    pageSize: new FormControl<number>(20, { nonNullable: true })
  });

  result: CallHistoryPagedResult = {
    items: [],
    page: 1,
    pageSize: 20,
    totalCount: 0,
    totalPages: 0,
    hasPreviousPage: false,
    hasNextPage: false
  };

  loading = false;
  errorMessage = '';
  page = 1;
  agents: AgentResponse[] = [];
  dispositions: CallDisposition[] = [];

  readonly CallStatus = CallStatus;
  readonly CallDirection = CallDirection;

  readonly statuses = Object.keys(CallStatus)
    .filter(k => Number.isNaN(Number(k)))
    .map(k => ({ label: k, value: (CallStatus as any)[k] as CallStatus }));

  get startIndex(): number {
    if (this.result.totalCount === 0) return 0;
    return (this.result.page - 1) * this.result.pageSize + 1;
  }

  get endIndex(): number {
    return Math.min(this.result.page * this.result.pageSize, this.result.totalCount);
  }

  ngOnInit(): void {
    this.loadFilterOptions();

    this.search.valueChanges
      .pipe(debounceTime(350), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.loadPage(1));

    this.filters.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.loadPage(1));

    this.loadPage(1);
  }

  loadFilterOptions(): void {
    if (this.agentService.getAll) {
      this.agentService.getAll().subscribe({
        next: a => {
          this.agents = a;
          this.cdr.markForCheck();
        },
        error: () => {}
      });
    }

    this.dispositionService.getDispositions(false).subscribe({
      next: d => {
        this.dispositions = d;
        this.cdr.markForCheck();
      },
      error: () => {}
    });
  }

  resetFilters(): void {
    this.search.setValue('');
    this.filters.reset({
      fromDate: '',
      toDate: '',
      direction: null,
      status: null,
      agentId: '',
      dispositionId: '',
      pageSize: 20
    });
    this.loadPage(1);
  }

  loadPage(page: number): void {
    this.page = page;
    this.loading = true;
    this.errorMessage = '';

    const v = this.filters.getRawValue();

    let fromUtc: string | null = null;
    if (v.fromDate) {
      const fromDateObj = new Date(v.fromDate);
      fromDateObj.setHours(0, 0, 0, 0);
      fromUtc = fromDateObj.toISOString();
    }

    let toUtc: string | null = null;
    if (v.toDate) {
      const toDateObj = new Date(v.toDate);
      toDateObj.setHours(23, 59, 59, 999);
      toUtc = toDateObj.toISOString();
    }

    this.service.getHistory({
      search: this.search.value.trim() || undefined,
      agentId: v.agentId || undefined,
      direction: v.direction ?? undefined,
      status: v.status ?? undefined,
      fromUtc: fromUtc ?? undefined,
      toUtc: toUtc ?? undefined,
      dispositionId: v.dispositionId || undefined,
      page,
      pageSize: v.pageSize || 20
    }).pipe(
      finalize(() => {
        this.loading = false;
        this.cdr.markForCheck();
      })
    ).subscribe({
      next: r => {
        this.result = r;
      },
      error: () => {
        this.errorMessage = 'Unable to load call history from the server. Please check your connection and retry.';
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
    const m = Math.floor(s / 60);
    const sec = s % 60;
    return `${String(m).padStart(2, '0')}:${String(sec).padStart(2, '0')}`;
  }

  truncate(str: string, len: number): string {
    if (!str) return '';
    return str.length > len ? str.substring(0, len) + '…' : str;
  }
}
