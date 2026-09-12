import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  DestroyRef,
  OnInit,
  inject
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { debounceTime, distinctUntilChanged, finalize } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  Agent,
  AgentDetails,
  AgentCallItem,
  CreateAgentRequest,
  UpdateAgentRequest
} from '../../core/models/agent.models';
import { AgentStatus, CallDirection, CallStatus } from '../../core/models/agent-dashboard.models';
import { AgentService } from '../../core/services/agent.service';
import { AuthService } from '../../core/auth/auth.service';

type DrawerTab = 'profile' | 'calls';

@Component({
  selector: 'app-agents',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  template: `
    <div class="agents-page">
      <!-- Header -->
      <div class="page-header">
        <div>
          <span class="eyebrow">Workforce Management</span>
          <h2>Agent Operations</h2>
          <p>Manage call center agents, team assignments, real-time availability, and security credentials.</p>
        </div>
        <div class="header-actions">
          @if (!loading) {
            <span class="count-badge">{{ agents.length }} agents total</span>
          }
          @if (canManageAgents()) {
            <button type="button" class="primary-button-sm" (click)="openCreateModal()">
              + Add Agent
            </button>
          }
        </div>
      </div>

      <!-- Metrics Cards -->
      <div class="metrics-row">
        <div class="metric-card">
          <span class="metric-label">Total Agents</span>
          <strong class="metric-value">{{ totalCount }}</strong>
          <span class="metric-sub">Registered staff</span>
        </div>
        <div class="metric-card">
          <span class="metric-label">Available</span>
          <strong class="metric-value text-success">{{ availableCount }}</strong>
          <span class="metric-sub">Ready for inbound</span>
        </div>
        <div class="metric-card">
          <span class="metric-label">Busy / In Call</span>
          <strong class="metric-value text-blue">{{ busyCount }}</strong>
          <span class="metric-sub">Handling calls</span>
        </div>
        <div class="metric-card">
          <span class="metric-label">Away / Break</span>
          <strong class="metric-value text-warning">{{ awayCount }}</strong>
          <span class="metric-sub">Temporarily stepped away</span>
        </div>
        <div class="metric-card">
          <span class="metric-label">Offline / Inactive</span>
          <strong class="metric-value text-muted">{{ offlineCount }}</strong>
          <span class="metric-sub">{{ inactiveCount }} deactivated</span>
        </div>
      </div>

      <!-- Toast Feedback -->
      @if (toastMessage) {
        <div class="toast-banner" [class.error]="toastIsError">
          <span>{{ toastMessage }}</span>
          <button type="button" class="toast-close" (click)="toastMessage = ''">✕</button>
        </div>
      }

      <!-- Search & Filter Bar -->
      <div class="search-panel card">
        <div class="search-row">
          <div class="search-input-wrapper">
            <span class="search-icon">🔍</span>
            <input
              type="text"
              class="search-input"
              [formControl]="searchInput"
              placeholder="Search by Employee Code, Name, Username, or Team..."
            />
            @if (searchInput.value) {
              <button type="button" class="clear-btn" (click)="searchInput.setValue('')">✕</button>
            }
          </div>

          <!-- Status Filter -->
          <div class="filter-group">
            <label for="statusFilter">Status:</label>
            <select id="statusFilter" class="filter-select" [formControl]="statusFilter">
              <option value="">All Statuses</option>
              <option [value]="AgentStatus.Available">Available</option>
              <option [value]="AgentStatus.Busy">Busy</option>
              <option [value]="AgentStatus.Away">Away</option>
              <option [value]="AgentStatus.Offline">Offline</option>
              <option [value]="AgentStatus.WrapUp">WrapUp</option>
            </select>
          </div>

          <!-- Active Filter -->
          <div class="filter-group">
            <label for="activeFilter">Active:</label>
            <select id="activeFilter" class="filter-select" [formControl]="activeFilter">
              <option value="">All States</option>
              <option value="true">Active Only</option>
              <option value="false">Deactivated Only</option>
            </select>
          </div>

          @if (hasActiveFilters()) {
            <button type="button" class="clear-filters-btn" (click)="resetFilters()">
              Clear Filters
            </button>
          }
        </div>
      </div>

      <!-- Agent Table -->
      <div class="table-card card">
        @if (loading) {
          <div class="loading-state">
            <div class="spinner"></div>
            <span>Loading agent roster...</span>
          </div>
        } @else if (agents.length === 0) {
          <div class="empty-state">
            <span class="empty-icon">👥</span>
            <h3>No agents found</h3>
            <p>No agent records matched the specified search or filter criteria.</p>
            @if (canManageAgents()) {
              <button type="button" class="primary-button-sm" (click)="openCreateModal()">
                Add First Agent
              </button>
            }
          </div>
        } @else {
          <div class="table-wrapper">
            <table>
              <thead>
                <tr>
                  <th>Agent</th>
                  <th>Employee Code</th>
                  <th>Role</th>
                  <th>Team</th>
                  <th>Live Status</th>
                  <th>Account State</th>
                  <th class="actions-col">Actions</th>
                </tr>
              </thead>
              <tbody>
                @for (agent of agents; track agent.id) {
                  <tr [class.row-deactivated]="!agent.isActive">
                    <td>
                      <div class="agent-profile-cell">
                        <div class="agent-avatar">{{ initials(agent.displayName) }}</div>
                        <div>
                          <strong>{{ agent.displayName }}</strong>
                          <small>{{ agent.userName || 'No username' }}</small>
                        </div>
                      </div>
                    </td>
                    <td>
                      <span class="code-badge">{{ agent.employeeCode }}</span>
                    </td>
                    <td>
                      <span
                        class="badge-role"
                        [class]="'badge-' + ((agent.roleName || 'Agent').toLowerCase())">
                        {{ agent.roleName || 'Agent' }}
                      </span>
                    </td>
                    <td>
                      @if (agent.team) {
                        <span class="team-tag">{{ agent.team }}</span>
                      } @else {
                        <span class="text-muted">—</span>
                      }
                    </td>
                    <td>
                      <div class="status-cell">
                        <span class="status-dot" [ngClass]="statusClass(agent.status)"></span>
                        <span class="status-label" [ngClass]="statusClass(agent.status)">
                          {{ statusLabel(agent.status) }}
                        </span>
                        @if (canManageAgents() && agent.isActive) {
                          <select
                            class="quick-status-select"
                            [value]="agent.status"
                            (change)="onQuickStatusChange(agent, $event)">
                            <option [value]="AgentStatus.Available">Available</option>
                            <option [value]="AgentStatus.Busy">Busy</option>
                            <option [value]="AgentStatus.Away">Away</option>
                            <option [value]="AgentStatus.Offline">Offline</option>
                            <option [value]="AgentStatus.WrapUp">WrapUp</option>
                          </select>
                        }
                      </div>
                    </td>
                    <td>
                      @if (agent.isActive) {
                        <span class="badge-active">Active</span>
                      } @else {
                        <span class="badge-inactive">Deactivated</span>
                      }
                    </td>
                    <td class="actions-col">
                      <div class="action-btn-group">
                        <button
                          type="button"
                          class="action-btn btn-details"
                          title="View Agent Details & Call History"
                          (click)="openDetails(agent)">
                          Details
                        </button>

                        @if (canManageAgents()) {
                          <button
                            type="button"
                            class="action-btn btn-edit"
                            title="Edit Agent Profile"
                            (click)="openEditModal(agent)">
                            Edit
                          </button>

                          @if (agent.isActive) {
                            <button
                              type="button"
                              class="action-btn btn-deactivate"
                              title="Deactivate Agent"
                              (click)="toggleActive(agent)">
                              Deactivate
                            </button>
                          } @else {
                            <button
                              type="button"
                              class="action-btn btn-reactivate"
                              title="Reactivate Agent"
                              (click)="toggleActive(agent)">
                              Reactivate
                            </button>
                          }
                        }

                        @if (canDeleteAgents()) {
                          <button
                            type="button"
                            class="action-btn btn-delete"
                            title="Delete Agent Record (Admin Only)"
                            (click)="openDeleteConfirm(agent)">
                            Delete
                          </button>
                        }
                      </div>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </div>

      <!-- CREATE AGENT MODAL -->
      @if (showCreateModal) {
        <div class="modal-backdrop" (click)="closeCreateModal()">
          <div class="modal-dialog" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <h3>Add New Agent</h3>
              <button type="button" class="modal-close" (click)="closeCreateModal()">✕</button>
            </div>
            <form [formGroup]="createForm" (ngSubmit)="submitCreate()">
              <div class="modal-body">
                @if (modalError) {
                  <div class="form-error-banner">{{ modalError }}</div>
                }

                <div class="form-row">
                  <div class="form-field">
                    <label for="c-employeeCode">Employee Code <span class="required">*</span></label>
                    <input
                      id="c-employeeCode"
                      type="text"
                      formControlName="employeeCode"
                      placeholder="e.g. AG-105"
                    />
                    @if (createForm.get('employeeCode')?.touched && createForm.get('employeeCode')?.invalid) {
                      <span class="field-error">Employee Code is required.</span>
                    }
                  </div>

                  <div class="form-field">
                    <label for="c-displayName">Full Name <span class="required">*</span></label>
                    <input
                      id="c-displayName"
                      type="text"
                      formControlName="displayName"
                      placeholder="e.g. Sarah Connor"
                    />
                    @if (createForm.get('displayName')?.touched && createForm.get('displayName')?.invalid) {
                      <span class="field-error">Display name is required (min 2 chars).</span>
                    }
                  </div>
                </div>

                <div class="form-row">
                  <div class="form-field">
                    <label for="c-team">Team / Department</label>
                    <input
                      id="c-team"
                      type="text"
                      formControlName="team"
                      placeholder="e.g. Customer Support, Sales, VIP"
                    />
                  </div>

                  <div class="form-field">
                    <label for="c-roleName">System Role <span class="required">*</span></label>
                    <select id="c-roleName" formControlName="roleName">
                      <option value="Agent">Agent</option>
                      <option value="Supervisor">Supervisor</option>
                      @if (isAdmin()) {
                        <option value="Admin">Admin</option>
                      }
                    </select>
                  </div>
                </div>

                <div class="form-section-title">Login Credentials</div>
                <div class="form-row">
                  <div class="form-field">
                    <label for="c-userName">Username <span class="required">*</span></label>
                    <input
                      id="c-userName"
                      type="text"
                      formControlName="userName"
                      placeholder="e.g. sarah.connor"
                    />
                    @if (createForm.get('userName')?.touched && createForm.get('userName')?.invalid) {
                      <span class="field-error">Username is required (min 3 chars).</span>
                    }
                  </div>

                  <div class="form-field">
                    <label for="c-password">Password <span class="required">*</span></label>
                    <input
                      id="c-password"
                      type="password"
                      formControlName="password"
                      placeholder="Min 6 characters"
                    />
                    @if (createForm.get('password')?.touched && createForm.get('password')?.invalid) {
                      <span class="field-error">Password is required (min 6 chars).</span>
                    }
                  </div>
                </div>
              </div>

              <div class="modal-footer">
                <button type="button" class="secondary-button" (click)="closeCreateModal()">
                  Cancel
                </button>
                <button
                  type="submit"
                  class="primary-button-sm"
                  [disabled]="createForm.invalid || modalSaving">
                  @if (modalSaving) {
                    <span>Creating...</span>
                  } @else {
                    <span>Create Agent</span>
                  }
                </button>
              </div>
            </form>
          </div>
        </div>
      }

      <!-- EDIT AGENT MODAL -->
      @if (showEditModal && editingAgent) {
        <div class="modal-backdrop" (click)="closeEditModal()">
          <div class="modal-dialog" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <h3>Edit Agent: {{ editingAgent.displayName }}</h3>
              <button type="button" class="modal-close" (click)="closeEditModal()">✕</button>
            </div>
            <form [formGroup]="editForm" (ngSubmit)="submitEdit()">
              <div class="modal-body">
                @if (modalError) {
                  <div class="form-error-banner">{{ modalError }}</div>
                }

                <div class="form-row">
                  <div class="form-field">
                    <label for="e-employeeCode">Employee Code <span class="required">*</span></label>
                    <input
                      id="e-employeeCode"
                      type="text"
                      formControlName="employeeCode"
                    />
                    @if (editForm.get('employeeCode')?.touched && editForm.get('employeeCode')?.invalid) {
                      <span class="field-error">Employee code is required.</span>
                    }
                  </div>

                  <div class="form-field">
                    <label for="e-displayName">Display Name <span class="required">*</span></label>
                    <input
                      id="e-displayName"
                      type="text"
                      formControlName="displayName"
                    />
                    @if (editForm.get('displayName')?.touched && editForm.get('displayName')?.invalid) {
                      <span class="field-error">Display name is required (min 2 chars).</span>
                    }
                  </div>
                </div>

                <div class="form-row">
                  <div class="form-field">
                    <label for="e-team">Team / Department</label>
                    <input
                      id="e-team"
                      type="text"
                      formControlName="team"
                      placeholder="e.g. VIP Desk"
                    />
                  </div>

                  <div class="form-field">
                    <label for="e-roleName">Role</label>
                    <select id="e-roleName" formControlName="roleName">
                      <option value="Agent">Agent</option>
                      <option value="Supervisor">Supervisor</option>
                      @if (isAdmin()) {
                        <option value="Admin">Admin</option>
                      }
                    </select>
                  </div>
                </div>

                <div class="form-row">
                  <div class="form-field">
                    <label for="e-password">Reset Password (leave blank to keep)</label>
                    <input
                      id="e-password"
                      type="password"
                      formControlName="password"
                      placeholder="Enter new password if changing"
                    />
                  </div>

                  <div class="form-field-checkbox">
                    <label>
                      <input type="checkbox" formControlName="isActive" />
                      <span>Account is Active</span>
                    </label>
                    <small class="field-hint">Deactivating prevents call routing and sets status to Offline.</small>
                  </div>
                </div>
              </div>

              <div class="modal-footer">
                <button type="button" class="secondary-button" (click)="closeEditModal()">
                  Cancel
                </button>
                <button
                  type="submit"
                  class="primary-button-sm"
                  [disabled]="editForm.invalid || modalSaving">
                  @if (modalSaving) {
                    <span>Saving...</span>
                  } @else {
                    <span>Save Changes</span>
                  }
                </button>
              </div>
            </form>
          </div>
        </div>
      }

      <!-- AGENT DETAILS DRAWER -->
      @if (showDetailsDrawer && selectedDetails) {
        <div class="drawer-backdrop" (click)="closeDetails()">
          <div class="drawer-panel" (click)="$event.stopPropagation()">
            <div class="drawer-header">
              <div class="drawer-title-group">
                <div class="agent-avatar large">{{ initials(selectedDetails.displayName) }}</div>
                <div>
                  <h3>{{ selectedDetails.displayName }}</h3>
                  <div class="drawer-sub-meta">
                    <span class="code-badge">{{ selectedDetails.employeeCode }}</span>
                    <span
                      class="badge-role"
                      [class]="'badge-' + ((selectedDetails.roleName || 'Agent').toLowerCase())">
                      {{ selectedDetails.roleName || 'Agent' }}
                    </span>
                    @if (selectedDetails.team) {
                      <span class="team-tag">{{ selectedDetails.team }}</span>
                    }
                  </div>
                </div>
              </div>
              <button type="button" class="modal-close" (click)="closeDetails()">✕</button>
            </div>

            <!-- Drawer Tabs -->
            <div class="drawer-tabs">
              <button
                type="button"
                class="tab-btn"
                [class.active]="activeTab === 'profile'"
                (click)="activeTab = 'profile'">
                Agent Profile
              </button>
              <button
                type="button"
                class="tab-btn"
                [class.active]="activeTab === 'calls'"
                (click)="loadAgentCallsTab()">
                Call History ({{ selectedDetails.totalAssignedCalls }})
              </button>
            </div>

            <div class="drawer-content">
              @if (activeTab === 'profile') {
                <div class="details-section">
                  <span class="section-label">Availability & Metrics</span>
                  <div class="drawer-metrics-grid">
                    <div class="d-metric">
                      <span>Live Status</span>
                      <strong class="status-label" [ngClass]="statusClass(selectedDetails.status)">
                        <span class="status-dot" [ngClass]="statusClass(selectedDetails.status)"></span>
                        {{ statusLabel(selectedDetails.status) }}
                      </strong>
                    </div>
                    <div class="d-metric">
                      <span>Total Calls</span>
                      <strong>{{ selectedDetails.totalAssignedCalls }}</strong>
                    </div>
                    <div class="d-metric">
                      <span>Completed</span>
                      <strong>{{ selectedDetails.completedCalls }}</strong>
                    </div>
                    <div class="d-metric">
                      <span>State</span>
                      <strong [class.text-success]="selectedDetails.isActive" [class.text-danger]="!selectedDetails.isActive">
                        {{ selectedDetails.isActive ? 'Active' : 'Deactivated' }}
                      </strong>
                    </div>
                  </div>
                </div>

                <div class="details-section">
                  <span class="section-label">Account Information</span>
                  <div class="profile-info-grid">
                    <div>
                      <span>Username</span>
                      <strong>{{ selectedDetails.userName || '—' }}</strong>
                    </div>
                    <div>
                      <span>System Role</span>
                      <strong>{{ selectedDetails.roleName || 'Agent' }}</strong>
                    </div>
                    <div>
                      <span>Assigned Team</span>
                      <strong>{{ selectedDetails.team || 'Unassigned' }}</strong>
                    </div>
                    <div>
                      <span>Agent ID</span>
                      <small class="mono">{{ selectedDetails.id }}</small>
                    </div>
                    <div>
                      <span>Created On</span>
                      <strong>{{ selectedDetails.createdAt | date:'medium' }}</strong>
                    </div>
                    <div>
                      <span>Last Updated</span>
                      <strong>{{ selectedDetails.updatedAt ? (selectedDetails.updatedAt | date:'medium') : '—' }}</strong>
                    </div>
                  </div>
                </div>

                @if (selectedDetails.recentCalls.length > 0) {
                  <div class="details-section">
                    <span class="section-label">Recent Activity</span>
                    <div class="mini-call-list">
                      @for (call of selectedDetails.recentCalls; track call.id) {
                        <div class="mini-call-item">
                          <div>
                            <strong>{{ call.phoneNumber }}</strong>
                            <small>{{ callDirectionLabel(call.direction) }} · {{ call.startedAt | date:'shortTime' }}</small>
                          </div>
                          <span class="badge-call-status" [ngClass]="'call-status-' + call.status">
                            {{ callStatusLabel(call.status) }}
                          </span>
                        </div>
                      }
                    </div>
                  </div>
                }
              } @else {
                <!-- Calls Tab -->
                <div class="calls-tab-view">
                  @if (callsLoading) {
                    <div class="loading-state">
                      <div class="spinner"></div>
                      <span>Loading call records...</span>
                    </div>
                  } @else if (agentCalls.length === 0) {
                    <div class="empty-state">
                      <span>☎</span>
                      <p>No call history recorded for this agent yet.</p>
                    </div>
                  } @else {
                    <div class="table-wrapper">
                      <table>
                        <thead>
                          <tr>
                            <th>Phone</th>
                            <th>Direction</th>
                            <th>Status</th>
                            <th>Started</th>
                            <th>Duration</th>
                          </tr>
                        </thead>
                        <tbody>
                          @for (call of agentCalls; track call.id) {
                            <tr>
                              <td><strong>{{ call.phoneNumber }}</strong></td>
                              <td>{{ callDirectionLabel(call.direction) }}</td>
                              <td>
                                <span class="badge-call-status" [ngClass]="'call-status-' + call.status">
                                  {{ callStatusLabel(call.status) }}
                                </span>
                              </td>
                              <td><small>{{ call.startedAt | date:'MMM d, h:mm a' }}</small></td>
                              <td><small>{{ formatDuration(call.startedAt, call.endedAt) }}</small></td>
                            </tr>
                          }
                        </tbody>
                      </table>
                    </div>
                  }
                </div>
              }
            </div>
          </div>
        </div>
      }

      <!-- DELETE CONFIRMATION MODAL (Admin Only) -->
      @if (showDeleteModal && deletingAgent) {
        <div class="modal-backdrop" (click)="closeDeleteConfirm()">
          <div class="modal-dialog delete-dialog" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <h3>Confirm Agent Deletion</h3>
              <button type="button" class="modal-close" (click)="closeDeleteConfirm()">✕</button>
            </div>
            <div class="modal-body">
              @if (modalError) {
                <div class="form-error-banner">{{ modalError }}</div>
              }
              <p>
                Are you sure you want to permanently delete agent
                <strong>{{ deletingAgent.displayName }}</strong> ({{ deletingAgent.employeeCode }})?
              </p>
              <div class="compliance-warning">
                <strong>🛡️ Enterprise Compliance Guard:</strong>
                <p>
                  If this agent has historical call records, the system will block hard deletion with HTTP 409 Conflict to preserve audit integrity. We recommend deactivating the agent instead.
                </p>
              </div>
            </div>
            <div class="modal-footer">
              <button type="button" class="secondary-button" (click)="closeDeleteConfirm()">
                Cancel
              </button>
              <button
                type="button"
                class="danger-button"
                [disabled]="modalSaving"
                (click)="confirmDelete()">
                @if (modalSaving) {
                  <span>Deleting...</span>
                } @else {
                  <span>Delete Agent</span>
                }
              </button>
            </div>
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    .agents-page {
      display: flex;
      flex-direction: column;
      gap: 20px;
    }

    .page-header {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: 16px;
    }

    .header-actions {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .count-badge {
      font-size: 13px;
      color: #64748b;
      background: #e2e8f0;
      padding: 6px 12px;
      border-radius: 20px;
      font-weight: 600;
    }

    .primary-button-sm {
      background: #0d1628;
      color: #fff;
      border: none;
      padding: 9px 16px;
      border-radius: 8px;
      font-weight: 650;
      font-size: 13px;
      cursor: pointer;
      display: inline-flex;
      align-items: center;
      gap: 6px;
      transition: background 0.15s;
    }
    .primary-button-sm:hover:not(:disabled) { background: #1e293b; }
    .primary-button-sm:disabled { opacity: 0.55; cursor: not-allowed; }

    .secondary-button {
      background: #fff;
      color: #334155;
      border: 1px solid #cbd5e1;
      padding: 9px 16px;
      border-radius: 8px;
      font-weight: 600;
      font-size: 13px;
      cursor: pointer;
    }
    .secondary-button:hover { background: #f8fafc; }

    .danger-button {
      background: #dc2626;
      color: #fff;
      border: none;
      padding: 9px 16px;
      border-radius: 8px;
      font-weight: 650;
      font-size: 13px;
      cursor: pointer;
    }
    .danger-button:hover:not(:disabled) { background: #b91c1c; }

    /* Metrics Grid */
    .metrics-row {
      display: grid;
      grid-template-columns: repeat(5, minmax(0, 1fr));
      gap: 14px;
    }
    @media (max-width: 1100px) { .metrics-row { grid-template-columns: repeat(3, 1fr); } }
    @media (max-width: 700px) { .metrics-row { grid-template-columns: 1fr; } }

    .metric-card {
      background: #fff;
      border: 1px solid #e2e8f0;
      border-radius: 12px;
      padding: 16px;
      display: flex;
      flex-direction: column;
      box-shadow: 0 1px 3px rgba(0,0,0,0.02);
    }
    .metric-label { font-size: 12px; color: #64748b; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em; }
    .metric-value { font-size: 26px; font-weight: 750; margin: 4px 0 2px; }
    .metric-sub { font-size: 11px; color: #94a3b8; }
    .text-success { color: #16a34a; }
    .text-blue { color: #2563eb; }
    .text-warning { color: #d97706; }
    .text-muted { color: #64748b; }
    .text-danger { color: #dc2626; }

    /* Toast */
    .toast-banner {
      background: #f0fdf4;
      border: 1px solid #bbf7d0;
      color: #166534;
      padding: 12px 16px;
      border-radius: 8px;
      display: flex;
      align-items: center;
      justify-content: space-between;
      font-size: 14px;
      font-weight: 500;
    }
    .toast-banner.error {
      background: #fef2f2;
      border-color: #fecaca;
      color: #991b1b;
    }
    .toast-close { background: none; border: none; font-size: 16px; cursor: pointer; color: inherit; }

    /* Search & Filter Panel */
    .search-panel { padding: 14px 18px; }
    .search-row {
      display: flex;
      align-items: center;
      gap: 14px;
      flex-wrap: wrap;
    }
    .search-input-wrapper {
      position: relative;
      flex: 1;
      min-width: 260px;
    }
    .search-icon { position: absolute; left: 12px; top: 50%; transform: translateY(-50%); font-size: 14px; opacity: 0.5; }
    .search-input {
      width: 100%;
      padding: 9px 36px 9px 34px;
      border: 1px solid #cbd5e1;
      border-radius: 8px;
      font-size: 14px;
      outline: none;
    }
    .search-input:focus { border-color: #0d1628; box-shadow: 0 0 0 2px rgba(13,22,40,0.08); }
    .clear-btn { position: absolute; right: 10px; top: 50%; transform: translateY(-50%); background: none; border: none; cursor: pointer; color: #94a3b8; font-size: 14px; }

    .filter-group { display: flex; align-items: center; gap: 6px; font-size: 13px; color: #475569; font-weight: 600; }
    .filter-select {
      padding: 8px 12px;
      border: 1px solid #cbd5e1;
      border-radius: 8px;
      font-size: 13px;
      background: #fff;
      outline: none;
    }
    .clear-filters-btn {
      background: #f1f5f9;
      border: 1px solid #cbd5e1;
      color: #475569;
      padding: 8px 12px;
      border-radius: 8px;
      font-size: 12px;
      font-weight: 600;
      cursor: pointer;
    }
    .clear-filters-btn:hover { background: #e2e8f0; }

    /* Table */
    .card { background: #fff; border: 1px solid #e2e8f0; border-radius: 12px; }
    .table-card { overflow: hidden; }
    .table-wrapper { overflow-x: auto; }
    table { width: 100%; border-collapse: collapse; text-align: left; font-size: 13px; }
    th {
      background: #f8fafc;
      padding: 12px 16px;
      font-weight: 700;
      color: #475569;
      font-size: 11px;
      text-transform: uppercase;
      letter-spacing: 0.06em;
      border-bottom: 1px solid #e2e8f0;
    }
    td { padding: 14px 16px; border-bottom: 1px solid #f1f5f9; vertical-align: middle; }
    tr:last-child td { border-bottom: none; }
    tr:hover td { background: #fafcff; }
    .row-deactivated td { opacity: 0.65; background: #fafafa; }

    .agent-profile-cell { display: flex; align-items: center; gap: 10px; }
    .agent-avatar {
      width: 34px;
      height: 34px;
      border-radius: 8px;
      background: #0d1628;
      color: #fff;
      display: grid;
      place-items: center;
      font-size: 12px;
      font-weight: 750;
      letter-spacing: 0.04em;
    }
    .agent-avatar.large { width: 44px; height: 44px; font-size: 15px; border-radius: 10px; }
    .agent-profile-cell strong { display: block; font-size: 13px; color: #0f172a; }
    .agent-profile-cell small { display: block; color: #64748b; font-size: 11px; }

    .code-badge {
      display: inline-block;
      font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
      font-size: 11px;
      font-weight: 700;
      padding: 3px 7px;
      background: #f1f5f9;
      color: #334155;
      border-radius: 5px;
      border: 1px solid #e2e8f0;
    }

    .team-tag {
      display: inline-block;
      font-size: 11px;
      font-weight: 600;
      color: #4338ca;
      background: #e0e7ff;
      padding: 3px 8px;
      border-radius: 6px;
    }

    .status-cell { display: flex; align-items: center; gap: 8px; }
    .status-dot { width: 8px; height: 8px; border-radius: 50%; }
    .status-dot.available { background: #22c55e; box-shadow: 0 0 0 2px rgba(34,197,94,0.2); }
    .status-dot.busy { background: #3b82f6; }
    .status-dot.away { background: #f59e0b; }
    .status-dot.offline { background: #94a3b8; }
    .status-dot.wrapup { background: #8b5cf6; }

    .status-label { font-size: 12px; font-weight: 650; }
    .status-label.available { color: #15803d; }
    .status-label.busy { color: #1d4ed8; }
    .status-label.away { color: #b45309; }
    .status-label.offline { color: #64748b; }
    .status-label.wrapup { color: #6d28d9; }

    .quick-status-select {
      font-size: 11px;
      padding: 2px 4px;
      border: 1px solid #cbd5e1;
      border-radius: 4px;
      background: #fff;
      cursor: pointer;
    }

    .badge-active {
      font-size: 11px;
      font-weight: 700;
      color: #15803d;
      background: #dcfce7;
      padding: 2px 8px;
      border-radius: 12px;
    }
    .badge-inactive {
      font-size: 11px;
      font-weight: 700;
      color: #b91c1c;
      background: #fee2e2;
      padding: 2px 8px;
      border-radius: 12px;
    }

    .actions-col { text-align: right; }
    .action-btn-group { display: inline-flex; align-items: center; gap: 6px; }
    .action-btn {
      font-size: 12px;
      font-weight: 600;
      padding: 5px 9px;
      border-radius: 6px;
      cursor: pointer;
      border: 1px solid transparent;
      background: #f8fafc;
      color: #334155;
      border-color: #cbd5e1;
      transition: all 0.12s;
    }
    .action-btn:hover { background: #f1f5f9; }
    .btn-details:hover { background: #eff6ff; color: #1d4ed8; border-color: #bfdbfe; }
    .btn-edit:hover { background: #fefce8; color: #a16207; border-color: #fef08a; }
    .btn-deactivate { color: #b45309; border-color: #fde68a; background: #fffbeb; }
    .btn-deactivate:hover { background: #fef3c7; }
    .btn-reactivate { color: #15803d; border-color: #bbf7d0; background: #f0fdf4; }
    .btn-reactivate:hover { background: #dcfce7; }
    .btn-delete { color: #dc2626; border-color: #fecaca; background: #fef2f2; }
    .btn-delete:hover { background: #fee2e2; }

    /* Empty & Loading */
    .loading-state, .empty-state {
      padding: 48px;
      text-align: center;
      color: #64748b;
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 12px;
    }
    .spinner {
      width: 28px;
      height: 28px;
      border: 3px solid #e2e8f0;
      border-top-color: #0d1628;
      border-radius: 50%;
      animation: spin 0.7s linear infinite;
    }
    @keyframes spin { to { transform: rotate(360deg); } }
    .empty-icon { font-size: 36px; opacity: 0.6; }

    /* Modals & Backdrop */
    .modal-backdrop {
      position: fixed;
      inset: 0;
      background: rgba(15,23,42,0.5);
      backdrop-filter: blur(2px);
      z-index: 999;
      display: grid;
      place-items: center;
      padding: 20px;
    }
    .modal-dialog {
      background: #fff;
      border-radius: 14px;
      width: min(560px, 100%);
      max-height: 90vh;
      overflow-y: auto;
      box-shadow: 0 20px 60px rgba(0,0,0,0.15);
      border: 1px solid #e2e8f0;
    }
    .modal-dialog.delete-dialog { width: min(460px, 100%); }
    .modal-header {
      padding: 18px 24px;
      border-bottom: 1px solid #e2e8f0;
      display: flex;
      align-items: center;
      justify-content: space-between;
    }
    .modal-header h3 { margin: 0; font-size: 17px; }
    .modal-close { background: none; border: none; font-size: 16px; cursor: pointer; color: #64748b; }
    .modal-body { padding: 22px 24px; display: flex; flex-direction: column; gap: 16px; }
    .modal-footer {
      padding: 16px 24px;
      border-top: 1px solid #e2e8f0;
      display: flex;
      justify-content: flex-end;
      gap: 10px;
      background: #f8fafc;
      border-bottom-left-radius: 14px;
      border-bottom-right-radius: 14px;
    }

    .form-row { display: grid; grid-template-columns: 1fr 1fr; gap: 14px; }
    @media (max-width: 500px) { .form-row { grid-template-columns: 1fr; } }
    .form-field { display: flex; flex-direction: column; gap: 6px; }
    .form-field label { font-size: 12px; font-weight: 650; color: #334155; }
    .form-field input, .form-field select {
      padding: 9px 12px;
      border: 1px solid #cbd5e1;
      border-radius: 8px;
      font-size: 13px;
      outline: none;
    }
    .form-field input:focus, .form-field select:focus {
      border-color: #0d1628;
      box-shadow: 0 0 0 2px rgba(13,22,40,0.08);
    }
    .form-field-checkbox { display: flex; flex-direction: column; gap: 4px; justify-content: center; }
    .form-field-checkbox label { display: flex; align-items: center; gap: 8px; font-size: 13px; font-weight: 600; cursor: pointer; }
    .field-hint { font-size: 11px; color: #64748b; }
    .form-section-title {
      font-size: 11px;
      font-weight: 750;
      text-transform: uppercase;
      letter-spacing: 0.08em;
      color: #64748b;
      margin-top: 4px;
    }
    .required { color: #dc2626; }
    .field-error { font-size: 11px; color: #dc2626; }
    .form-error-banner {
      background: #fef2f2;
      border: 1px solid #fecaca;
      color: #991b1b;
      padding: 10px 14px;
      border-radius: 8px;
      font-size: 13px;
    }
    .compliance-warning {
      background: #fffbeb;
      border: 1px solid #fde68a;
      border-radius: 8px;
      padding: 12px;
      font-size: 12px;
      color: #92400e;
      margin-top: 10px;
    }
    .compliance-warning strong { display: block; margin-bottom: 4px; color: #78350f; }

    /* Drawer */
    .drawer-backdrop {
      position: fixed;
      inset: 0;
      background: rgba(15,23,42,0.4);
      z-index: 998;
      display: flex;
      justify-content: flex-end;
    }
    .drawer-panel {
      width: min(580px, 100%);
      height: 100%;
      background: #fff;
      box-shadow: -10px 0 40px rgba(0,0,0,0.1);
      display: flex;
      flex-direction: column;
      animation: slideIn 0.2s ease-out;
    }
    @keyframes slideIn { from { transform: translateX(100%); } to { transform: translateX(0); } }
    .drawer-header {
      padding: 20px 24px;
      border-bottom: 1px solid #e2e8f0;
      display: flex;
      align-items: center;
      justify-content: space-between;
    }
    .drawer-title-group { display: flex; align-items: center; gap: 12px; }
    .drawer-title-group h3 { margin: 0; font-size: 17px; }
    .drawer-sub-meta { display: flex; align-items: center; gap: 8px; margin-top: 4px; }
    .drawer-tabs {
      display: flex;
      border-bottom: 1px solid #e2e8f0;
      background: #f8fafc;
      padding: 0 16px;
    }
    .tab-btn {
      padding: 12px 18px;
      border: none;
      background: none;
      font-size: 13px;
      font-weight: 650;
      color: #64748b;
      cursor: pointer;
      border-bottom: 2px solid transparent;
    }
    .tab-btn.active { color: #0d1628; border-bottom-color: #0d1628; background: #fff; }
    .drawer-content { flex: 1; overflow-y: auto; padding: 24px; display: flex; flex-direction: column; gap: 20px; }

    .details-section { display: flex; flex-direction: column; gap: 10px; }
    .drawer-metrics-grid {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: 10px;
    }
    .d-metric {
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      padding: 12px;
      border-radius: 8px;
      text-align: center;
    }
    .d-metric span { display: block; font-size: 11px; color: #64748b; text-transform: uppercase; font-weight: 600; }
    .d-metric strong { display: flex; align-items: center; justify-content: center; gap: 6px; font-size: 16px; margin-top: 4px; }

    .profile-info-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 12px;
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      border-radius: 10px;
      padding: 16px;
    }
    .profile-info-grid span { display: block; font-size: 11px; color: #64748b; font-weight: 600; text-transform: uppercase; }
    .profile-info-grid strong { display: block; font-size: 13px; color: #1e293b; margin-top: 2px; }
    .mono { font-family: ui-monospace, monospace; font-size: 11px; word-break: break-all; }

    .mini-call-list { display: flex; flex-direction: column; gap: 8px; }
    .mini-call-item {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 10px 14px;
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      border-radius: 8px;
    }
    .mini-call-item strong { display: block; font-size: 13px; }
    .mini-call-item small { color: #64748b; font-size: 11px; }

    .badge-call-status {
      font-size: 11px;
      font-weight: 700;
      padding: 3px 8px;
      border-radius: 6px;
    }
    .call-status-4 { background: #dcfce7; color: #15803d; } /* Completed */
    .call-status-3 { background: #dbeafe; color: #1d4ed8; } /* Connected */
    .call-status-2 { background: #fef3c7; color: #b45309; } /* Ringing */
    .call-status-5 { background: #fee2e2; color: #b91c1c; } /* Abandoned */
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AgentsComponent implements OnInit {
  private readonly agentService = inject(AgentService);
  readonly auth = inject(AuthService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly destroyRef = inject(DestroyRef);

  readonly AgentStatus = AgentStatus;

  // Search & Filters
  readonly searchInput = new FormControl('', { nonNullable: true });
  readonly statusFilter = new FormControl('', { nonNullable: true });
  readonly activeFilter = new FormControl('', { nonNullable: true });

  // State
  agents: Agent[] = [];
  loading = false;
  toastMessage = '';
  toastIsError = false;

  // Modals state
  showCreateModal = false;
  showEditModal = false;
  showDetailsDrawer = false;
  showDeleteModal = false;
  modalSaving = false;
  modalError = '';

  // Current entity targets
  editingAgent: Agent | null = null;
  deletingAgent: Agent | null = null;
  selectedDetails: AgentDetails | null = null;
  activeTab: DrawerTab = 'profile';
  agentCalls: AgentCallItem[] = [];
  callsLoading = false;

  // Forms
  readonly createForm = new FormGroup({
    employeeCode: new FormControl('', [Validators.required, Validators.maxLength(50)]),
    displayName: new FormControl('', [Validators.required, Validators.minLength(2), Validators.maxLength(150)]),
    team: new FormControl(''),
    roleName: new FormControl('Agent', [Validators.required]),
    userName: new FormControl('', [Validators.required, Validators.minLength(3)]),
    password: new FormControl('', [Validators.required, Validators.minLength(6)])
  });

  readonly editForm = new FormGroup({
    employeeCode: new FormControl('', [Validators.required, Validators.maxLength(50)]),
    displayName: new FormControl('', [Validators.required, Validators.minLength(2), Validators.maxLength(150)]),
    team: new FormControl(''),
    roleName: new FormControl('Agent'),
    password: new FormControl(''),
    isActive: new FormControl(true)
  });

  // Metrics getters
  get totalCount(): number { return this.agents.length; }
  get availableCount(): number { return this.agents.filter(a => a.status === AgentStatus.Available && a.isActive).length; }
  get busyCount(): number { return this.agents.filter(a => a.status === AgentStatus.Busy && a.isActive).length; }
  get awayCount(): number { return this.agents.filter(a => a.status === AgentStatus.Away && a.isActive).length; }
  get offlineCount(): number { return this.agents.filter(a => a.status === AgentStatus.Offline || !a.isActive).length; }
  get inactiveCount(): number { return this.agents.filter(a => !a.isActive).length; }

  ngOnInit(): void {
    this.loadAgents();

    // Debounce search input
    this.searchInput.valueChanges
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.loadAgents());

    this.statusFilter.valueChanges
      .pipe(distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.loadAgents());

    this.activeFilter.valueChanges
      .pipe(distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.loadAgents());
  }

  loadAgents(): void {
    this.loading = true;
    this.cdr.markForCheck();

    const statusVal = this.statusFilter.value ? (Number(this.statusFilter.value) as AgentStatus) : '';
    const activeVal = this.activeFilter.value ? this.activeFilter.value === 'true' : '';

    this.agentService
      .getAll({
        search: this.searchInput.value,
        status: statusVal,
        isActive: activeVal
      })
      .pipe(
        finalize(() => {
          this.loading = false;
          this.cdr.markForCheck();
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: data => {
          this.agents = data;
          this.cdr.markForCheck();
        },
        error: err => {
          this.showToast(err?.error?.message || 'Failed to load agents list.', true);
        }
      });
  }

  hasActiveFilters(): boolean {
    return !!(this.searchInput.value || this.statusFilter.value || this.activeFilter.value);
  }

  resetFilters(): void {
    this.searchInput.setValue('', { emitEvent: false });
    this.statusFilter.setValue('', { emitEvent: false });
    this.activeFilter.setValue('', { emitEvent: false });
    this.loadAgents();
  }

  // Permissions checks
  canManageAgents(): boolean {
    return this.auth.hasRole(['Admin', 'Supervisor']);
  }

  canDeleteAgents(): boolean {
    return this.auth.hasRole(['Admin']);
  }

  isAdmin(): boolean {
    return this.auth.hasRole(['Admin']);
  }

  // Quick Status changer
  onQuickStatusChange(agent: Agent, event: Event): void {
    const select = event.target as HTMLSelectElement;
    const newStatus = Number(select.value) as AgentStatus;
    if (newStatus === agent.status) return;

    this.agentService.updateStatus(agent.id, newStatus).subscribe({
      next: updated => {
        agent.status = updated.status;
        this.showToast(`Updated status of ${agent.displayName} to ${this.statusLabel(newStatus)}.`);
        this.cdr.markForCheck();
      },
      error: err => {
        select.value = String(agent.status);
        this.showToast(err?.error?.message || 'Failed to update agent status.', true);
        this.cdr.markForCheck();
      }
    });
  }

  // Deactivate / Reactivate
  toggleActive(agent: Agent): void {
    const obs$ = agent.isActive
      ? this.agentService.deactivate(agent.id)
      : this.agentService.reactivate(agent.id);

    obs$.subscribe({
      next: updated => {
        agent.isActive = updated.isActive;
        agent.status = updated.status;
        this.showToast(
          updated.isActive
            ? `Agent ${agent.displayName} has been reactivated.`
            : `Agent ${agent.displayName} has been deactivated.`
        );
        this.cdr.markForCheck();
      },
      error: err => {
        this.showToast(err?.error?.message || 'Failed to update active state.', true);
      }
    });
  }

  // Create Modal
  openCreateModal(): void {
    this.modalError = '';
    this.modalSaving = false;
    this.createForm.reset({ roleName: 'Agent' });
    this.showCreateModal = true;
    this.cdr.markForCheck();
  }

  closeCreateModal(): void {
    this.showCreateModal = false;
    this.cdr.markForCheck();
  }

  submitCreate(): void {
    if (this.createForm.invalid) return;
    this.modalSaving = true;
    this.modalError = '';

    const req: CreateAgentRequest = {
      employeeCode: this.createForm.value.employeeCode!.trim(),
      displayName: this.createForm.value.displayName!.trim(),
      team: this.createForm.value.team?.trim() || null,
      roleName: this.createForm.value.roleName || 'Agent',
      userName: this.createForm.value.userName!.trim(),
      password: this.createForm.value.password!
    };

    this.agentService.create(req).subscribe({
      next: () => {
        this.showCreateModal = false;
        this.modalSaving = false;
        this.showToast('Agent account created successfully.');
        this.loadAgents();
      },
      error: err => {
        this.modalSaving = false;
        this.modalError = err?.error?.message || 'Failed to create agent. Please check form inputs.';
        this.cdr.markForCheck();
      }
    });
  }

  // Edit Modal
  openEditModal(agent: Agent): void {
    this.editingAgent = agent;
    this.modalError = '';
    this.modalSaving = false;
    this.editForm.patchValue({
      employeeCode: agent.employeeCode,
      displayName: agent.displayName,
      team: agent.team || '',
      roleName: agent.roleName || 'Agent',
      password: '',
      isActive: agent.isActive
    });
    this.showEditModal = true;
    this.cdr.markForCheck();
  }

  closeEditModal(): void {
    this.showEditModal = false;
    this.editingAgent = null;
    this.cdr.markForCheck();
  }

  submitEdit(): void {
    if (!this.editingAgent || this.editForm.invalid) return;
    this.modalSaving = true;
    this.modalError = '';

    const req: UpdateAgentRequest = {
      employeeCode: this.editForm.value.employeeCode!.trim(),
      displayName: this.editForm.value.displayName!.trim(),
      team: this.editForm.value.team?.trim() || null,
      roleName: this.editForm.value.roleName || 'Agent',
      isActive: this.editForm.value.isActive ?? true,
      password: this.editForm.value.password?.trim() || null
    };

    this.agentService.update(this.editingAgent.id, req).subscribe({
      next: updated => {
        this.showEditModal = false;
        this.modalSaving = false;
        this.showToast(`Agent ${updated.displayName} updated successfully.`);
        this.loadAgents();
      },
      error: err => {
        this.modalSaving = false;
        this.modalError = err?.error?.message || 'Failed to update agent.';
        this.cdr.markForCheck();
      }
    });
  }

  // Details Drawer
  openDetails(agent: Agent): void {
    this.activeTab = 'profile';
    this.selectedDetails = null;
    this.agentCalls = [];
    this.showDetailsDrawer = true;
    this.cdr.markForCheck();

    this.agentService.getDetails(agent.id).subscribe({
      next: details => {
        this.selectedDetails = details;
        this.cdr.markForCheck();
      },
      error: err => {
        this.showToast(err?.error?.message || 'Failed to load agent details.', true);
        this.closeDetails();
      }
    });
  }

  closeDetails(): void {
    this.showDetailsDrawer = false;
    this.selectedDetails = null;
    this.cdr.markForCheck();
  }

  loadAgentCallsTab(): void {
    this.activeTab = 'calls';
    if (!this.selectedDetails) return;
    this.callsLoading = true;
    this.cdr.markForCheck();

    this.agentService
      .getCalls(this.selectedDetails.id, 1, 20)
      .pipe(
        finalize(() => {
          this.callsLoading = false;
          this.cdr.markForCheck();
        })
      )
      .subscribe({
        next: calls => {
          this.agentCalls = calls;
          this.cdr.markForCheck();
        },
        error: err => {
          this.showToast(err?.error?.message || 'Failed to load call history.', true);
        }
      });
  }

  // Delete Modal
  openDeleteConfirm(agent: Agent): void {
    this.deletingAgent = agent;
    this.modalError = '';
    this.modalSaving = false;
    this.showDeleteModal = true;
    this.cdr.markForCheck();
  }

  closeDeleteConfirm(): void {
    this.showDeleteModal = false;
    this.deletingAgent = null;
    this.cdr.markForCheck();
  }

  confirmDelete(): void {
    if (!this.deletingAgent) return;
    this.modalSaving = true;
    this.modalError = '';

    this.agentService.delete(this.deletingAgent.id).subscribe({
      next: () => {
        this.showDeleteModal = false;
        this.modalSaving = false;
        this.showToast('Agent deleted successfully.');
        this.loadAgents();
      },
      error: err => {
        this.modalSaving = false;
        this.modalError = err?.error?.message || 'Deletion failed. Historical call records may reference this agent.';
        this.cdr.markForCheck();
      }
    });
  }

  // Helpers
  showToast(msg: string, isError = false): void {
    this.toastMessage = msg;
    this.toastIsError = isError;
    this.cdr.markForCheck();
    setTimeout(() => {
      if (this.toastMessage === msg) {
        this.toastMessage = '';
        this.cdr.markForCheck();
      }
    }, 4500);
  }

  initials(name: string): string {
    return name
      .split(' ')
      .filter(Boolean)
      .slice(0, 2)
      .map(part => part[0])
      .join('')
      .toUpperCase();
  }

  statusClass(status: AgentStatus): string {
    switch (status) {
      case AgentStatus.Available: return 'available';
      case AgentStatus.Busy: return 'busy';
      case AgentStatus.Away: return 'away';
      case AgentStatus.WrapUp: return 'wrapup';
      default: return 'offline';
    }
  }

  statusLabel(status: AgentStatus): string {
    switch (status) {
      case AgentStatus.Available: return 'Available';
      case AgentStatus.Busy: return 'Busy';
      case AgentStatus.Away: return 'Away';
      case AgentStatus.WrapUp: return 'WrapUp';
      default: return 'Offline';
    }
  }

  callDirectionLabel(direction: number): string {
    return direction === CallDirection.Inbound ? 'Inbound' : 'Outbound';
  }

  callStatusLabel(status: number): string {
    return CallStatus[status] ?? 'Unknown';
  }

  formatDuration(startStr: string, endStr?: string | null): string {
    if (!endStr) return 'Active';
    const start = new Date(startStr).getTime();
    const end = new Date(endStr).getTime();
    const sec = Math.max(0, Math.floor((end - start) / 1000));
    const mins = Math.floor(sec / 60);
    const remSec = sec % 60;
    return `${mins}m ${remSec}s`;
  }
}
