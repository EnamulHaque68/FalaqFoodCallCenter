import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  OnInit,
  inject
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import {
  UserListItem,
  RoleItem,
  CreateUserRequest,
  UpdateUserRequest,
  ResetPasswordRequest
} from '../../core/models/user.models';
import { UserService } from '../../core/services/user.service';
import { AuthService } from '../../core/auth/auth.service';

@Component({
  selector: 'app-users',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="users-page">
      <!-- Header -->
      <div class="page-header">
        <div>
          <span class="eyebrow">Identity & Access Management</span>
          <h2>User Accounts & Security</h2>
          <p>Administer call center logins, roles, account statuses, credentials, and agent linkages.</p>
        </div>
        <div class="header-actions">
          <button type="button" class="button secondary" [disabled]="loading" (click)="loadUsers()">
            ↻ Refresh
          </button>
          <button type="button" class="button primary" (click)="openCreateModal()">
            + Add User
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
      @if (successMessage) {
        <div class="success-banner">
          <span>✓ {{ successMessage }}</span>
          <button type="button" class="close-btn" (click)="successMessage = ''">✕</button>
        </div>
      }

      <!-- Metric Summary Cards -->
      <div class="metrics-row">
        <div class="metric-card">
          <span class="metric-label">Total Accounts</span>
          <strong class="metric-value">{{ users.length }}</strong>
          <span class="metric-sub">Registered users</span>
        </div>
        <div class="metric-card">
          <span class="metric-label">Active Logins</span>
          <strong class="metric-value text-success">{{ activeCount }}</strong>
          <span class="metric-sub">Permitted access</span>
        </div>
        <div class="metric-card">
          <span class="metric-label">Deactivated</span>
          <strong class="metric-value text-danger">{{ deactivatedCount }}</strong>
          <span class="metric-sub">Disabled accounts</span>
        </div>
        <div class="metric-card">
          <span class="metric-label">Administrators</span>
          <strong class="metric-value text-purple">{{ adminCount }}</strong>
          <span class="metric-sub">Full system control</span>
        </div>
        <div class="metric-card">
          <span class="metric-label">Agents & Staff</span>
          <strong class="metric-value text-blue">{{ agentCount }}</strong>
          <span class="metric-sub">Telephony workforce</span>
        </div>
      </div>

      <!-- Search & Filters -->
      <div class="filters-bar">
        <div class="search-input-wrap">
          <span class="search-icon">🔍</span>
          <input
            type="text"
            placeholder="Search username, agent name or code…"
            [formControl]="searchControl"
            (keyup.enter)="applyFilters()"
          />
        </div>

        <div class="filter-group">
          <select [formControl]="roleFilterControl" (change)="applyFilters()">
            <option value="">All Roles</option>
            @for (role of roles; track role.id) {
              <option [value]="role.name">{{ role.name }}</option>
            }
          </select>

          <select [formControl]="statusFilterControl" (change)="applyFilters()">
            <option value="">All Statuses</option>
            <option value="true">Active Only</option>
            <option value="false">Deactivated Only</option>
          </select>

          @if (hasActiveFilters()) {
            <button type="button" class="clear-filters-btn" (click)="resetFilters()">
              Clear Filters
            </button>
          }
        </div>
      </div>

      <!-- Users Table -->
      <div class="table-container">
        @if (loading) {
          <div class="loading-state">
            <span class="spinner"></span>
            <p>Loading user directory…</p>
          </div>
        } @else if (users.length === 0) {
          <div class="empty-state">
            <span class="empty-icon">👥</span>
            <h3>No users found</h3>
            <p>No user accounts matched your current filter criteria.</p>
          </div>
        } @else {
          <table class="users-table">
            <thead>
              <tr>
                <th>User Account</th>
                <th>Assigned Role</th>
                <th>Account Status</th>
                <th>Linked Agent</th>
                <th>Last Login</th>
                <th>Created Date</th>
                <th class="text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              @for (u of users; track u.id) {
                <tr [class.row-inactive]="!u.isActive">
                  <td>
                    <div class="user-cell">
                      <div class="user-avatar" [class]="'avatar-' + getRoleClass(u.roleName)">
                        {{ u.userName.slice(0, 2).toUpperCase() }}
                      </div>
                      <div class="user-info">
                        <strong>{{ u.userName }}</strong>
                        @if (isCurrentLoggedInUser(u.id)) {
                          <span class="badge-self">You</span>
                        }
                      </div>
                    </div>
                  </td>
                  <td>
                    <span class="badge-role" [class]="'badge-' + getRoleClass(u.roleName)">
                      {{ u.roleName }}
                    </span>
                  </td>
                  <td>
                    @if (u.isActive) {
                      <span class="status-pill status-active">
                        <span class="dot dot-active"></span> Active
                      </span>
                    } @else {
                      <span class="status-pill status-deactivated">
                        <span class="dot dot-deactivated"></span> Deactivated
                      </span>
                    }
                  </td>
                  <td>
                    @if (u.agentId) {
                      <div class="agent-link-info">
                        <span class="agent-name">{{ u.agentName || 'Agent' }}</span>
                        <small class="agent-code">{{ u.employeeCode || '—' }}</small>
                        @if (u.team) {
                          <span class="team-tag">{{ u.team }}</span>
                        }
                      </div>
                    } @else {
                      <span class="text-muted">— (None)</span>
                    }
                  </td>
                  <td>
                    @if (u.lastLoginAt) {
                      <span class="timestamp">{{ u.lastLoginAt | date:'medium' }}</span>
                    } @else {
                      <span class="text-muted">Never logged in</span>
                    }
                  </td>
                  <td>
                    <span class="timestamp-sub">{{ u.createdAt | date:'shortDate' }}</span>
                  </td>
                  <td class="text-right">
                    <div class="actions-group">
                      <button
                        type="button"
                        class="btn-action"
                        title="Edit User Details"
                        (click)="openEditModal(u)">
                        ✏️ Edit
                      </button>
                      <button
                        type="button"
                        class="btn-action"
                        title="Reset Password"
                        (click)="openResetPasswordModal(u)">
                        🔑 Reset
                      </button>
                      @if (u.isActive) {
                        <button
                          type="button"
                          class="btn-action btn-danger-action"
                          [disabled]="isCurrentLoggedInUser(u.id)"
                          [title]="isCurrentLoggedInUser(u.id) ? 'Cannot deactivate your own account' : 'Deactivate user'"
                          (click)="toggleActiveStatus(u)">
                          Deactivate
                        </button>
                      } @else {
                        <button
                          type="button"
                          class="btn-action btn-success-action"
                          title="Reactivate user"
                          (click)="toggleActiveStatus(u)">
                          Reactivate
                        </button>
                      }
                    </div>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        }
      </div>

      <!-- Modal: Create User -->
      @if (showCreateModal) {
        <div class="modal-backdrop" (click)="closeModals()">
          <div class="modal-dialog" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <h3>Create User Account</h3>
              <button type="button" class="close-btn" (click)="closeModals()">✕</button>
            </div>
            <form [formGroup]="createForm" (ngSubmit)="submitCreate()">
              <div class="modal-body">
                <label>
                  Username *
                  <input type="text" formControlName="userName" placeholder="e.g. jdoe" />
                  @if (createForm.get('userName')?.touched && createForm.get('userName')?.invalid) {
                    <span class="form-error">Username is required (min 3 characters).</span>
                  }
                </label>

                <label>
                  Initial Password *
                  <input type="password" formControlName="password" placeholder="At least 8 characters" />
                  @if (createForm.get('password')?.touched && createForm.get('password')?.invalid) {
                    <span class="form-error">Password must be at least 8 characters.</span>
                  }
                </label>

                <label>
                  Assigned Role *
                  <select formControlName="roleName">
                    @for (role of roles; track role.id) {
                      <option [value]="role.name">{{ role.name }} ({{ role.description }})</option>
                    }
                  </select>
                </label>

                <!-- Optional Agent provisioning fields if role == Agent -->
                @if (createForm.get('roleName')?.value === 'Agent') {
                  <div class="agent-provision-box">
                    <h4>Agent Profile Provisioning</h4>
                    <p>An Agent record will be automatically created and linked to this user login.</p>

                    <label>
                      Display Name
                      <input type="text" formControlName="displayName" placeholder="e.g. Jane Doe" />
                    </label>

                    <label>
                      Employee Code
                      <input type="text" formControlName="employeeCode" placeholder="e.g. AGT-1001 (optional, auto-generated)" />
                    </label>

                    <label>
                      Team
                      <input type="text" formControlName="team" placeholder="e.g. Tier 1 Support" />
                    </label>
                  </div>
                }
              </div>

              <div class="modal-footer">
                <button type="button" class="button secondary" (click)="closeModals()">Cancel</button>
                <button type="submit" class="button primary" [disabled]="createForm.invalid || submitting">
                  {{ submitting ? 'Creating…' : 'Create User' }}
                </button>
              </div>
            </form>
          </div>
        </div>
      }

      <!-- Modal: Edit User -->
      @if (showEditModal && editingUser) {
        <div class="modal-backdrop" (click)="closeModals()">
          <div class="modal-dialog" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <h3>Edit User: {{ editingUser.userName }}</h3>
              <button type="button" class="close-btn" (click)="closeModals()">✕</button>
            </div>
            <form [formGroup]="editForm" (ngSubmit)="submitEdit()">
              <div class="modal-body">
                <label>
                  Username *
                  <input type="text" formControlName="userName" />
                  @if (editForm.get('userName')?.touched && editForm.get('userName')?.invalid) {
                    <span class="form-error">Username is required.</span>
                  }
                </label>

                <label>
                  Assigned Role *
                  <select formControlName="roleName">
                    @for (role of roles; track role.id) {
                      <option [value]="role.name">{{ role.name }}</option>
                    }
                  </select>
                </label>

                <label class="checkbox-label">
                  <input type="checkbox" formControlName="isActive" />
                  Account Active (Permitted to log in)
                </label>

                @if (editingUser.agentId || editForm.get('roleName')?.value === 'Agent') {
                  <div class="agent-provision-box">
                    <h4>Agent Synchronization</h4>
                    <label>
                      Display Name
                      <input type="text" formControlName="displayName" placeholder="Agent Display Name" />
                    </label>
                    <label>
                      Team
                      <input type="text" formControlName="team" placeholder="Team Name" />
                    </label>
                  </div>
                }
              </div>

              <div class="modal-footer">
                <button type="button" class="button secondary" (click)="closeModals()">Cancel</button>
                <button type="submit" class="button primary" [disabled]="editForm.invalid || submitting">
                  {{ submitting ? 'Saving…' : 'Save Changes' }}
                </button>
              </div>
            </form>
          </div>
        </div>
      }

      <!-- Modal: Reset Password -->
      @if (showResetModal && resettingUser) {
        <div class="modal-backdrop" (click)="closeModals()">
          <div class="modal-dialog" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <h3>Reset Password: {{ resettingUser.userName }}</h3>
              <button type="button" class="close-btn" (click)="closeModals()">✕</button>
            </div>
            <form [formGroup]="resetForm" (ngSubmit)="submitResetPassword()">
              <div class="modal-body">
                <p>Set a new password for <strong>{{ resettingUser.userName }}</strong>. They can use this immediately to log in.</p>
                <label>
                  New Password *
                  <input type="password" formControlName="newPassword" placeholder="Minimum 8 characters" />
                  @if (resetForm.get('newPassword')?.touched && resetForm.get('newPassword')?.invalid) {
                    <span class="form-error">Password must be at least 8 characters.</span>
                  }
                </label>
              </div>

              <div class="modal-footer">
                <button type="button" class="button secondary" (click)="closeModals()">Cancel</button>
                <button type="submit" class="button danger" [disabled]="resetForm.invalid || submitting">
                  {{ submitting ? 'Resetting…' : 'Reset Password' }}
                </button>
              </div>
            </form>
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    .users-page {
      display: flex;
      flex-direction: column;
      gap: 20px;
    }

    .page-header {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      gap: 16px;
    }

    .page-header h2 {
      margin: 4px 0 6px;
      font-size: 24px;
      color: #0f172a;
    }

    .page-header p {
      margin: 0;
      color: #64748b;
      font-size: 14px;
    }

    .eyebrow {
      font-size: 12px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.05em;
      color: #6366f1;
    }

    .header-actions {
      display: flex;
      gap: 10px;
    }

    /* Buttons */
    .button {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      border: 0;
      border-radius: 8px;
      padding: 9px 16px;
      font-weight: 600;
      font-size: 13px;
      cursor: pointer;
      transition: background 0.15s ease, transform 0.05s ease;
    }

    .button:active {
      transform: scale(0.98);
    }

    .button.primary {
      background: #0f172a;
      color: #fff;
    }
    .button.primary:hover {
      background: #1e293b;
    }

    .button.secondary {
      background: #e2e8f0;
      color: #1e293b;
    }
    .button.secondary:hover {
      background: #cbd5e1;
    }

    .button.danger {
      background: #ef4444;
      color: white;
    }
    .button.danger:hover {
      background: #dc2626;
    }

    /* Feedback Banners */
    .error-banner, .success-banner {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 12px 16px;
      border-radius: 8px;
      font-size: 13px;
      font-weight: 500;
    }

    .error-banner {
      background: #fef2f2;
      border: 1px solid #fecaca;
      color: #991b1b;
    }

    .success-banner {
      background: #ecfdf5;
      border: 1px solid #a7f3d0;
      color: #065f46;
    }

    .close-btn {
      background: none;
      border: none;
      font-size: 16px;
      cursor: pointer;
      color: inherit;
      padding: 0 4px;
    }

    /* Metrics Grid */
    .metrics-row {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
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
      font-size: 12px;
      font-weight: 600;
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
    .text-danger { color: #ef4444 !important; }
    .text-purple { color: #8b5cf6 !important; }
    .text-blue { color: #3b82f6 !important; }
    .text-muted { color: #94a3b8; font-size: 12px; }

    /* Filters Bar */
    .filters-bar {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 14px;
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
      min-width: 320px;
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
      gap: 10px;
    }

    .filter-group select {
      border: 1px solid #cbd5e1;
      border-radius: 8px;
      padding: 7px 12px;
      font-size: 13px;
      background: #fff;
      color: #1e293b;
      outline: none;
    }

    .clear-filters-btn {
      background: none;
      border: none;
      color: #6366f1;
      font-size: 13px;
      font-weight: 600;
      cursor: pointer;
      padding: 4px 8px;
    }

    /* Table Container */
    .table-container {
      background: #fff;
      border: 1px solid #e2e8f0;
      border-radius: 10px;
      overflow-x: auto;
      box-shadow: 0 1px 3px rgba(0,0,0,0.02);
    }

    .users-table {
      width: 100%;
      border-collapse: collapse;
      text-align: left;
      font-size: 13px;
    }

    .users-table th {
      background: #f8fafc;
      padding: 12px 16px;
      font-size: 11px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.05em;
      color: #64748b;
      border-bottom: 1px solid #e2e8f0;
    }

    .users-table td {
      padding: 14px 16px;
      border-bottom: 1px solid #f1f5f9;
      color: #1e293b;
      vertical-align: middle;
    }

    .row-inactive td {
      background: #fafafa;
      opacity: 0.8;
    }

    .user-cell {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .user-avatar {
      width: 36px;
      height: 36px;
      border-radius: 8px;
      display: flex;
      align-items: center;
      justify-content: center;
      font-weight: 700;
      font-size: 13px;
      flex-shrink: 0;
    }

    .avatar-admin { background: #ede9fe; color: #6d28d9; }
    .avatar-supervisor { background: #e0f2fe; color: #0369a1; }
    .avatar-agent { background: #ecfdf5; color: #047857; }

    .user-info {
      display: flex;
      align-items: center;
      gap: 6px;
    }

    .badge-self {
      font-size: 10px;
      font-weight: 700;
      padding: 2px 6px;
      border-radius: 4px;
      background: #e2e8f0;
      color: #334155;
    }

    /* Badges */
    .badge-role {
      display: inline-block;
      padding: 3px 8px;
      border-radius: 6px;
      font-size: 11px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.03em;
    }

    .badge-admin { background: #ede9fe; color: #6d28d9; border: 1px solid #ddd6fe; }
    .badge-supervisor { background: #e0f2fe; color: #0369a1; border: 1px solid #bae6fd; }
    .badge-agent { background: #ecfdf5; color: #047857; border: 1px solid #a7f3d0; }

    .status-pill {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      font-size: 12px;
      font-weight: 600;
      padding: 3px 8px;
      border-radius: 12px;
    }

    .status-active { background: #ecfdf5; color: #065f46; }
    .status-deactivated { background: #fef2f2; color: #991b1b; }

    .dot {
      width: 6px;
      height: 6px;
      border-radius: 50%;
    }
    .dot-active { background: #10b981; }
    .dot-deactivated { background: #ef4444; }

    .agent-link-info {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }
    .agent-name { font-weight: 600; font-size: 12px; color: #0f172a; }
    .agent-code { font-size: 11px; color: #64748b; font-family: monospace; }
    .team-tag { font-size: 10px; color: #475569; background: #f1f5f9; border-radius: 4px; padding: 1px 5px; width: fit-content; }

    .timestamp { font-size: 12px; color: #334155; }
    .timestamp-sub { font-size: 12px; color: #64748b; }

    .text-right { text-align: right; }

    /* Actions */
    .actions-group {
      display: inline-flex;
      align-items: center;
      gap: 6px;
    }

    .btn-action {
      background: #f8fafc;
      border: 1px solid #cbd5e1;
      border-radius: 6px;
      padding: 5px 9px;
      font-size: 11px;
      font-weight: 600;
      color: #334155;
      cursor: pointer;
      transition: all 0.15s ease;
    }

    .btn-action:hover:not(:disabled) {
      background: #e2e8f0;
      color: #0f172a;
    }

    .btn-action:disabled {
      opacity: 0.5;
      cursor: not-allowed;
    }

    .btn-danger-action {
      color: #dc2626;
      border-color: #fca5a5;
      background: #fff5f5;
    }
    .btn-danger-action:hover:not(:disabled) {
      background: #fee2e2;
    }

    .btn-success-action {
      color: #16a34a;
      border-color: #86efac;
      background: #f0fdf4;
    }
    .btn-success-action:hover:not(:disabled) {
      background: #dcfce7;
    }

    /* States */
    .loading-state, .empty-state {
      padding: 48px;
      text-align: center;
      color: #64748b;
    }

    .empty-icon { font-size: 36px; margin-bottom: 8px; display: block; }

    .spinner {
      width: 24px;
      height: 24px;
      border: 3px solid #cbd5e1;
      border-top-color: #0f172a;
      border-radius: 50%;
      display: inline-block;
      animation: spin 0.7s linear infinite;
    }
    @keyframes spin { to { transform: rotate(360deg); } }

    /* Modals */
    .modal-backdrop {
      position: fixed;
      inset: 0;
      background: rgba(15, 23, 42, 0.45);
      backdrop-filter: blur(2px);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 999;
      padding: 16px;
    }

    .modal-dialog {
      background: #fff;
      border-radius: 12px;
      box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.1), 0 8px 10px -6px rgba(0, 0, 0, 0.1);
      width: 100%;
      max-width: 480px;
      max-height: 90vh;
      overflow-y: auto;
      display: flex;
      flex-direction: column;
    }

    .modal-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 18px 20px;
      border-bottom: 1px solid #e2e8f0;
    }
    .modal-header h3 { margin: 0; font-size: 17px; color: #0f172a; }

    .modal-body {
      padding: 20px;
      display: flex;
      flex-direction: column;
      gap: 14px;
    }

    .modal-body label {
      display: flex;
      flex-direction: column;
      gap: 5px;
      font-size: 13px;
      font-weight: 600;
      color: #334155;
    }

    .modal-body input:not([type=checkbox]), .modal-body select {
      border: 1px solid #cbd5e1;
      border-radius: 7px;
      padding: 9px 12px;
      font-size: 13px;
      outline: none;
      background: #fff;
    }

    .modal-body input:focus, .modal-body select:focus {
      border-color: #6366f1;
      box-shadow: 0 0 0 2px rgba(99, 102, 241, 0.15);
    }

    .checkbox-label {
      flex-direction: row !important;
      align-items: center;
      gap: 8px !important;
      font-weight: 500 !important;
      margin-top: 6px;
    }

    .agent-provision-box {
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      border-radius: 8px;
      padding: 14px;
      display: flex;
      flex-direction: column;
      gap: 10px;
    }
    .agent-provision-box h4 { margin: 0; font-size: 13px; color: #0f172a; }
    .agent-provision-box p { margin: 0; font-size: 12px; color: #64748b; }

    .form-error {
      font-size: 11px;
      color: #ef4444;
      font-weight: 500;
    }

    .modal-footer {
      display: flex;
      justify-content: flex-end;
      gap: 10px;
      padding: 16px 20px;
      border-top: 1px solid #e2e8f0;
      background: #f8fafc;
      border-bottom-left-radius: 12px;
      border-bottom-right-radius: 12px;
    }
  `]
})
export class UsersComponent implements OnInit {
  private readonly userService = inject(UserService);
  readonly auth = inject(AuthService);
  private readonly cdr = inject(ChangeDetectorRef);

  users: UserListItem[] = [];
  roles: RoleItem[] = [];
  loading = false;
  submitting = false;
  errorMessage = '';
  successMessage = '';

  // Filters
  readonly searchControl = new FormControl('');
  readonly roleFilterControl = new FormControl('');
  readonly statusFilterControl = new FormControl('');

  // Modals state
  showCreateModal = false;
  showEditModal = false;
  showResetModal = false;

  editingUser: UserListItem | null = null;
  resettingUser: UserListItem | null = null;

  // Forms
  readonly createForm = new FormGroup({
    userName: new FormControl('', [Validators.required, Validators.minLength(3)]),
    password: new FormControl('', [Validators.required, Validators.minLength(8)]),
    roleName: new FormControl('Agent', [Validators.required]),
    displayName: new FormControl(''),
    employeeCode: new FormControl(''),
    team: new FormControl('')
  });

  readonly editForm = new FormGroup({
    userName: new FormControl('', [Validators.required, Validators.minLength(3)]),
    roleName: new FormControl('Agent', [Validators.required]),
    isActive: new FormControl(true),
    displayName: new FormControl(''),
    team: new FormControl('')
  });

  readonly resetForm = new FormGroup({
    newPassword: new FormControl('', [Validators.required, Validators.minLength(8)])
  });

  get activeCount(): number {
    return this.users.filter(u => u.isActive).length;
  }

  get deactivatedCount(): number {
    return this.users.filter(u => !u.isActive).length;
  }

  get adminCount(): number {
    return this.users.filter(u => u.roleName?.toLowerCase() === 'admin').length;
  }

  get agentCount(): number {
    return this.users.filter(u => u.roleName?.toLowerCase() === 'agent').length;
  }

  ngOnInit(): void {
    this.loadRoles();
    this.loadUsers();
  }

  loadRoles(): void {
    this.userService.getRoles().subscribe({
      next: roles => {
        this.roles = roles;
        this.cdr.markForCheck();
      },
      error: () => {
        // Default fallback roles if endpoint unreachable
        this.roles = [
          { id: '1', name: 'Admin', description: 'System administrator', permissions: [] },
          { id: '2', name: 'Supervisor', description: 'Call center supervisor', permissions: [] },
          { id: '3', name: 'Agent', description: 'Call center agent', permissions: [] }
        ];
        this.cdr.markForCheck();
      }
    });
  }

  loadUsers(): void {
    this.loading = true;
    this.errorMessage = '';

    const filter = {
      search: this.searchControl.value || undefined,
      role: this.roleFilterControl.value || undefined,
      isActive: this.statusFilterControl.value || undefined
    };

    this.userService.getAll(filter).pipe(
      finalize(() => {
        this.loading = false;
        this.cdr.markForCheck();
      })
    ).subscribe({
      next: data => {
        this.users = data;
        this.cdr.markForCheck();
      },
      error: err => {
        this.errorMessage = err?.error?.message || 'Failed to load user list.';
        this.cdr.markForCheck();
      }
    });
  }

  applyFilters(): void {
    this.loadUsers();
  }

  hasActiveFilters(): boolean {
    return !!(this.searchControl.value || this.roleFilterControl.value || this.statusFilterControl.value);
  }

  resetFilters(): void {
    this.searchControl.setValue('');
    this.roleFilterControl.setValue('');
    this.statusFilterControl.setValue('');
    this.loadUsers();
  }

  isCurrentLoggedInUser(userId: string): boolean {
    const current = this.auth.currentUser();
    return current?.id === userId;
  }

  getRoleClass(roleName: string): string {
    switch (roleName?.toLowerCase()) {
      case 'admin': return 'admin';
      case 'supervisor': return 'supervisor';
      default: return 'agent';
    }
  }

  // Create
  openCreateModal(): void {
    this.createForm.reset({
      userName: '',
      password: '',
      roleName: 'Agent',
      displayName: '',
      employeeCode: '',
      team: ''
    });
    this.showCreateModal = true;
  }

  submitCreate(): void {
    if (this.createForm.invalid) return;

    this.submitting = true;
    this.errorMessage = '';

    const req: CreateUserRequest = {
      userName: this.createForm.value.userName!.trim(),
      password: this.createForm.value.password!,
      roleName: this.createForm.value.roleName!,
      displayName: this.createForm.value.displayName?.trim() || undefined,
      employeeCode: this.createForm.value.employeeCode?.trim() || undefined,
      team: this.createForm.value.team?.trim() || undefined
    };

    this.userService.create(req).pipe(
      finalize(() => {
        this.submitting = false;
        this.cdr.markForCheck();
      })
    ).subscribe({
      next: newUser => {
        this.showCreateModal = false;
        this.successMessage = `User "${newUser.userName}" successfully created.`;
        this.loadUsers();
      },
      error: err => {
        this.errorMessage = err?.error?.message || 'Failed to create user.';
        this.cdr.markForCheck();
      }
    });
  }

  // Edit
  openEditModal(user: UserListItem): void {
    this.editingUser = user;
    this.editForm.reset({
      userName: user.userName,
      roleName: user.roleName,
      isActive: user.isActive,
      displayName: user.agentName || '',
      team: user.team || ''
    });
    this.showEditModal = true;
  }

  submitEdit(): void {
    if (this.editForm.invalid || !this.editingUser) return;

    this.submitting = true;
    this.errorMessage = '';

    const req: UpdateUserRequest = {
      userName: this.editForm.value.userName!.trim(),
      roleName: this.editForm.value.roleName!,
      isActive: !!this.editForm.value.isActive,
      displayName: this.editForm.value.displayName?.trim() || undefined,
      team: this.editForm.value.team?.trim() || undefined
    };

    this.userService.update(this.editingUser.id, req).pipe(
      finalize(() => {
        this.submitting = false;
        this.cdr.markForCheck();
      })
    ).subscribe({
      next: updated => {
        this.showEditModal = false;
        this.editingUser = null;
        this.successMessage = `User "${updated.userName}" successfully updated.`;
        this.loadUsers();
      },
      error: err => {
        this.errorMessage = err?.error?.message || 'Failed to update user.';
        this.cdr.markForCheck();
      }
    });
  }

  // Deactivate / Reactivate
  toggleActiveStatus(user: UserListItem): void {
    const action = user.isActive ? 'deactivate' : 'reactivate';
    if (!confirm(`Are you sure you want to ${action} user "${user.userName}"?`)) return;

    this.errorMessage = '';
    const obs$ = user.isActive
      ? this.userService.deactivate(user.id)
      : this.userService.reactivate(user.id);

    obs$.subscribe({
      next: updated => {
        this.successMessage = `User "${updated.userName}" ${action}d successfully.`;
        this.loadUsers();
      },
      error: err => {
        this.errorMessage = err?.error?.message || `Failed to ${action} user.`;
        this.cdr.markForCheck();
      }
    });
  }

  // Reset Password
  openResetPasswordModal(user: UserListItem): void {
    this.resettingUser = user;
    this.resetForm.reset({ newPassword: '' });
    this.showResetModal = true;
  }

  submitResetPassword(): void {
    if (this.resetForm.invalid || !this.resettingUser) return;

    this.submitting = true;
    this.errorMessage = '';

    const req: ResetPasswordRequest = {
      newPassword: this.resetForm.value.newPassword!
    };

    this.userService.resetPassword(this.resettingUser.id, req).pipe(
      finalize(() => {
        this.submitting = false;
        this.cdr.markForCheck();
      })
    ).subscribe({
      next: updated => {
        this.showResetModal = false;
        this.resettingUser = null;
        this.successMessage = `Password for user "${updated.userName}" has been reset.`;
        this.cdr.markForCheck();
      },
      error: err => {
        this.errorMessage = err?.error?.message || 'Failed to reset password.';
        this.cdr.markForCheck();
      }
    });
  }

  closeModals(): void {
    this.showCreateModal = false;
    this.showEditModal = false;
    this.showResetModal = false;
    this.editingUser = null;
    this.resettingUser = null;
  }
}
