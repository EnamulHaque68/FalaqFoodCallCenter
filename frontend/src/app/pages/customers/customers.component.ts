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
import { Router } from '@angular/router';
import { debounceTime, distinctUntilChanged, finalize, startWith } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  Customer,
  CustomerCallItem,
  CustomerPagedResult
} from '../../core/models/customer.models';
import { CustomerService } from '../../core/services/customer.service';
import { CallService } from '../../core/services/call.service';
import { IncomingCallSessionService } from '../../core/services/incoming-call-session.service';
import { CallCenterRealtimeService } from '../../core/services/call-center-realtime.service';
import { AuthService } from '../../core/auth/auth.service';
import { CallDirection, CallStatus } from '../../core/models/agent-dashboard.models';

type ActiveTab = 'profile' | 'calls';

@Component({
  selector: 'app-customers',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  template: `
    <div class="customers-page">
      <!-- Header -->
      <div class="page-header">
        <div>
          <span class="eyebrow">Customer Operations</span>
          <h2>Customer Directory</h2>
          <p>Lookup, manage, and call customers with enterprise RBAC controls.</p>
        </div>
        <div class="header-actions">
          @if (!loading) {
            <span class="count-badge">{{ result.totalCount }} total records</span>
          }
          @if (canManageCustomers()) {
            <button type="button" class="primary-button-sm" (click)="openCreateModal()">
              + Add Customer
            </button>
          }
        </div>
      </div>

      <!-- Toast Feedback -->
      @if (successMessage) {
        <div class="toast-success">
          <span>✓ {{ successMessage }}</span>
          <button type="button" (click)="successMessage = ''">×</button>
        </div>
      }
      @if (errorMessage) {
        <div class="error-banner">
          <span>{{ errorMessage }}</span>
          <button type="button" (click)="loadPage(page)">Retry</button>
        </div>
      }

      <!-- Toolbar -->
      <section class="toolbar">
        <div class="search-box">
          <span class="search-icon">⌕</span>
          <input
            [formControl]="searchControl"
            type="search"
            placeholder="Search by name, phone, email, CRM ID, or notes…"
            aria-label="Search customers" />
          @if (searchControl.value) {
            <button type="button" class="clear-search" (click)="searchControl.setValue('')">×</button>
          }
        </div>

        <div class="quick-lookup-box">
          <input
            [formControl]="lookupControl"
            type="text"
            placeholder="Direct Phone Lookup…"
            (keyup.enter)="performPhoneLookup()" />
          <button
            type="button"
            class="lookup-btn"
            [disabled]="lookupLoading || !lookupControl.value.trim()"
            (click)="performPhoneLookup()">
            {{ lookupLoading ? 'Searching…' : 'Lookup' }}
          </button>
        </div>

        <button type="button" class="refresh-button" [disabled]="loading" (click)="loadPage(page)">
          Refresh
        </button>
      </section>

      <!-- Customer List Card -->
      <section class="content-card">
        @if (loading) {
          <div class="table-state">
            <div class="spinner"></div>
            <strong>Loading customer directory…</strong>
            <span>Fetching verified records from secure customer API.</span>
          </div>
        } @else if (result.items.length === 0) {
          <div class="table-state">
            <span class="empty-icon">👥</span>
            <strong>No customers found</strong>
            <span>Try adjusting your search query or phone lookup.</span>
            @if (canManageCustomers()) {
              <button type="button" class="primary-button-sm" style="margin-top: 12px;" (click)="openCreateModal()">
                Create New Customer
              </button>
            }
          </div>
        } @else {
          <div class="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Customer</th>
                  <th>Phone Number</th>
                  <th>Email</th>
                  <th>CRM ID</th>
                  <th>Notes</th>
                  <th>Registered</th>
                  <th class="actions-col">Actions</th>
                </tr>
              </thead>
              <tbody>
                @for (customer of result.items; track customer.id) {
                  <tr>
                    <td>
                      <button class="customer-name-btn" type="button" (click)="selectCustomer(customer)">
                        <span class="avatar">{{ initials(customer.fullName) }}</span>
                        <div class="name-meta">
                          <strong>{{ customer.fullName }}</strong>
                          <small>{{ customer.id }}</small>
                        </div>
                      </button>
                    </td>
                    <td>
                      <span class="phone-text">{{ customer.phone }}</span>
                    </td>
                    <td>{{ customer.email || '—' }}</td>
                    <td>
                      @if (customer.crmCustomerId) {
                        <span class="crm-badge">{{ customer.crmCustomerId }}</span>
                      } @else {
                        <span class="muted-dash">—</span>
                      }
                    </td>
                    <td>
                      <span class="notes-preview" [title]="customer.notes || ''">
                        {{ customer.notes || '—' }}
                      </span>
                    </td>
                    <td>{{ customer.createdAt | date:'mediumDate' }}</td>
                    <td class="row-actions">
                      <button
                        type="button"
                        class="call-btn"
                        [disabled]="callingCustomerId === customer.id"
                        (click)="callCustomer(customer)"
                        title="Start outbound call">
                        {{ callingCustomerId === customer.id ? 'Calling…' : '📞 Call' }}
                      </button>
                      <button
                        type="button"
                        class="action-btn"
                        (click)="selectCustomer(customer)"
                        title="View details & call history">
                        View
                      </button>
                      @if (canManageCustomers()) {
                        <button
                          type="button"
                          class="action-btn"
                          (click)="openEditModal(customer)"
                          title="Edit customer details">
                          Edit
                        </button>
                      }
                      @if (canDeleteCustomers()) {
                        <button
                          type="button"
                          class="action-btn delete-btn"
                          (click)="confirmDelete(customer)"
                          title="Delete customer record">
                          Delete
                        </button>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>

          <!-- Pagination -->
          <div class="pagination">
            <span class="pagination-info">
              Showing page {{ result.page }} of {{ result.totalPages || 1 }} ({{ result.totalCount }} customers)
            </span>
            <div class="pagination-controls">
              <button
                type="button"
                [disabled]="!result.hasPreviousPage || loading"
                (click)="loadPage(result.page - 1)">
                ← Previous
              </button>
              <span class="current-page-pill">{{ result.page }}</span>
              <button
                type="button"
                [disabled]="!result.hasNextPage || loading"
                (click)="loadPage(result.page + 1)">
                Next →
              </button>
            </div>
          </div>
        }
      </section>

      <!-- Customer Details & Call History Drawer -->
      @if (selectedCustomer) {
        <div class="overlay" (click)="closeDetails()">
          <aside class="details-panel" (click)="$event.stopPropagation()">
            <div class="details-header">
              <div class="details-title">
                <span class="eyebrow">Customer Profile</span>
                <h3>{{ selectedCustomer.fullName }}</h3>
              </div>
              <button type="button" class="close-btn" (click)="closeDetails()">×</button>
            </div>

            <div class="profile-hero">
              <div class="detail-avatar">{{ initials(selectedCustomer.fullName) }}</div>
              <div class="hero-info">
                <h4>{{ selectedCustomer.fullName }}</h4>
                <p>{{ selectedCustomer.phone }}</p>
                @if (selectedCustomer.crmCustomerId) {
                  <span class="crm-badge">CRM: {{ selectedCustomer.crmCustomerId }}</span>
                }
              </div>
            </div>

            <!-- Call Outbound Button -->
            <button
              type="button"
              class="panel-call-button"
              [disabled]="callingCustomerId === selectedCustomer.id"
              (click)="callCustomer(selectedCustomer)">
              {{ callingCustomerId === selectedCustomer.id ? 'Connecting outbound call…' : '📞 Initiate Call to Customer' }}
            </button>
            @if (callError) {
              <div class="error-inline">{{ callError }}</div>
            }

            <!-- Drawer Tabs -->
            <div class="drawer-tabs">
              <button
                type="button"
                [class.active]="activeTab === 'profile'"
                (click)="activeTab = 'profile'">
                Profile & Notes
              </button>
              <button
                type="button"
                [class.active]="activeTab === 'calls'"
                (click)="loadCustomerCallHistory(selectedCustomer.id)">
                Call History ({{ customerCalls.length }})
              </button>
            </div>

            <!-- Profile Details Tab -->
            @if (activeTab === 'profile') {
              <div class="detail-list">
                <div>
                  <span>Full Name</span>
                  <strong>{{ selectedCustomer.fullName }}</strong>
                </div>
                <div>
                  <span>Phone</span>
                  <strong>{{ selectedCustomer.phone }}</strong>
                </div>
                <div>
                  <span>Email Address</span>
                  <strong>{{ selectedCustomer.email || '—' }}</strong>
                </div>
                <div>
                  <span>Physical Address</span>
                  <strong>{{ selectedCustomer.address || '—' }}</strong>
                </div>
                <div>
                  <span>CRM Customer ID</span>
                  <strong>{{ selectedCustomer.crmCustomerId || '—' }}</strong>
                </div>
                <div>
                  <span>Customer Notes</span>
                  <div class="notes-box">{{ selectedCustomer.notes || 'No notes on file for this customer.' }}</div>
                </div>
                <div>
                  <span>Record Created</span>
                  <small>{{ selectedCustomer.createdAt | date:'medium' }}</small>
                </div>
                <div>
                  <span>Last Updated</span>
                  <small>{{ selectedCustomer.updatedAt ? (selectedCustomer.updatedAt | date:'medium') : '—' }}</small>
                </div>
              </div>

              @if (canManageCustomers()) {
                <div class="panel-footer-actions">
                  <button type="button" class="edit-btn-large" (click)="openEditModal(selectedCustomer)">
                    ✏️ Edit Customer
                  </button>
                  @if (canDeleteCustomers()) {
                    <button type="button" class="delete-btn-large" (click)="confirmDelete(selectedCustomer)">
                      🗑️ Delete Customer
                    </button>
                  }
                </div>
              }
            }

            <!-- Call History Tab -->
            @if (activeTab === 'calls') {
              <div class="call-history-tab">
                @if (callsLoading) {
                  <div class="tab-spinner">
                    <div class="spinner"></div>
                    <span>Loading call history…</span>
                  </div>
                } @else if (customerCalls.length === 0) {
                  <div class="tab-empty">
                    <span>📞</span>
                    <p>No recorded calls found for this customer.</p>
                  </div>
                } @else {
                  <div class="calls-timeline">
                    @for (call of customerCalls; track call.id) {
                      <div class="call-card">
                        <div class="call-card-header">
                          <span class="call-direction" [class]="'dir-' + directionClass(call.direction)">
                            {{ directionLabel(call.direction) }}
                          </span>
                          <span class="call-status" [class]="'status-' + statusClass(call.status)">
                            {{ statusLabel(call.status) }}
                          </span>
                        </div>
                        <div class="call-card-meta">
                          <span><strong>Date:</strong> {{ call.startedAt | date:'medium' }}</span>
                          @if (call.durationSeconds) {
                            <span><strong>Duration:</strong> {{ call.durationSeconds }}s</span>
                          }
                          @if (call.callDispositionId) {
                            <span><strong>Disposition:</strong> {{ call.callDispositionId }}</span>
                          }
                        </div>
                      </div>
                    }
                  </div>
                }
              </div>
            }
          </aside>
        </div>
      }

      <!-- Add / Edit Customer Modal -->
      @if (showFormModal) {
        <div class="overlay modal-overlay" (click)="closeFormModal()">
          <div class="modal-card" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <div>
                <span class="eyebrow">{{ isEditing ? 'Update Record' : 'New Customer' }}</span>
                <h3>{{ isEditing ? 'Edit Customer Details' : 'Add New Customer' }}</h3>
              </div>
              <button type="button" class="close-btn" (click)="closeFormModal()">×</button>
            </div>

            @if (formError) {
              <div class="form-error-banner">
                <strong>Error:</strong> {{ formError }}
              </div>
            }

            <form [formGroup]="customerForm" (ngSubmit)="submitForm()" novalidate>
              <div class="form-group">
                <label for="fullName">Full Name <span class="required">*</span></label>
                <input
                  id="fullName"
                  type="text"
                  formControlName="fullName"
                  placeholder="e.g. Ayesha Rahman" />
                @if (customerForm.controls.fullName.touched && customerForm.controls.fullName.invalid) {
                  <small class="field-error">Full name is required (at least 2 characters).</small>
                }
              </div>

              <div class="form-group">
                <label for="phone">Phone Number <span class="required">*</span></label>
                <input
                  id="phone"
                  type="text"
                  formControlName="phone"
                  placeholder="e.g. 01712345678" />
                @if (customerForm.controls.phone.touched && customerForm.controls.phone.invalid) {
                  <small class="field-error">A valid phone number is required (at least 7 digits).</small>
                }
              </div>

              <div class="form-group">
                <label for="email">Email Address</label>
                <input
                  id="email"
                  type="email"
                  formControlName="email"
                  placeholder="e.g. ayesha@example.com" />
                @if (customerForm.controls.email.touched && customerForm.controls.email.invalid) {
                  <small class="field-error">Please enter a valid email address.</small>
                }
              </div>

              <div class="form-group">
                <label for="address">Delivery / Physical Address</label>
                <input
                  id="address"
                  type="text"
                  formControlName="address"
                  placeholder="e.g. House 42, Road 7, Banani, Dhaka" />
              </div>

              <div class="form-group">
                <label for="crmCustomerId">CRM Customer ID</label>
                <input
                  id="crmCustomerId"
                  type="text"
                  formControlName="crmCustomerId"
                  placeholder="e.g. CRM-88492" />
              </div>

              <div class="form-group">
                <label for="notes">Customer Notes / Preferences</label>
                <textarea
                  id="notes"
                  rows="3"
                  formControlName="notes"
                  placeholder="Important instructions, allergy notes, dietary preferences…"></textarea>
              </div>

              <div class="modal-actions">
                <button type="button" class="cancel-btn" (click)="closeFormModal()">Cancel</button>
                <button
                  type="submit"
                  class="save-btn"
                  [disabled]="customerForm.invalid || formSaving">
                  {{ formSaving ? 'Saving…' : (isEditing ? 'Save Changes' : 'Create Customer') }}
                </button>
              </div>
            </form>
          </div>
        </div>
      }

      <!-- Delete Confirmation Dialog -->
      @if (customerToDelete) {
        <div class="overlay modal-overlay" (click)="cancelDelete()">
          <div class="modal-card delete-dialog" (click)="$event.stopPropagation()">
            <div class="delete-icon">⚠️</div>
            <h3>Delete Customer Record?</h3>
            <p>
              Are you sure you want to permanently delete <strong>{{ customerToDelete.fullName }}</strong> ({{ customerToDelete.phone }})?
            </p>
            <p class="delete-warning">
              This action cannot be undone. Backend security constraints prevent deleting customers with associated call history.
            </p>

            @if (deleteError) {
              <div class="form-error-banner">
                {{ deleteError }}
              </div>
            }

            <div class="modal-actions">
              <button type="button" class="cancel-btn" (click)="cancelDelete()" [disabled]="deleteLoading">
                Cancel
              </button>
              <button
                type="button"
                class="confirm-delete-btn"
                [disabled]="deleteLoading"
                (click)="executeDelete()">
                {{ deleteLoading ? 'Deleting…' : 'Yes, Delete Customer' }}
              </button>
            </div>
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    .customers-page { max-width: 1280px; }
    .page-header { display: flex; align-items: flex-start; justify-content: space-between; gap: 20px; margin-bottom: 20px; }
    .page-header h2 { margin: 5px 0 4px; font-size: 28px; color: #0f172a; }
    .page-header p { margin: 0; color: #64748b; font-size: 13px; }
    .header-actions { display: flex; align-items: center; gap: 12px; }
    .count-badge { padding: 6px 12px; border: 1px solid #cbd5e1; background: #fff; border-radius: 999px; font-size: 12px; font-weight: 700; color: #475569; }
    .primary-button-sm { background: #0f172a; color: #fff; border: 0; border-radius: 8px; padding: 8px 14px; font-size: 12px; font-weight: 700; cursor: pointer; transition: background 0.15s; }
    .primary-button-sm:hover { background: #1e293b; }

    .toast-success { display: flex; justify-content: space-between; align-items: center; background: #ecfdf5; border: 1px solid #a7f3d0; color: #065f46; padding: 10px 14px; border-radius: 8px; margin-bottom: 16px; font-size: 13px; font-weight: 600; }
    .toast-success button { background: none; border: 0; font-size: 18px; color: #065f46; cursor: pointer; }
    .error-banner { display: flex; justify-content: space-between; align-items: center; gap: 10px; margin-bottom: 16px; padding: 10px 14px; border: 1px solid #fecdd3; background: #fff1f2; color: #9f1239; border-radius: 8px; font-size: 13px; }
    .error-banner button { border: 0; background: transparent; color: #9f1239; font-weight: 700; cursor: pointer; }

    .toolbar { display: flex; gap: 12px; margin-bottom: 18px; flex-wrap: wrap; }
    .search-box { display: flex; align-items: center; gap: 8px; flex: 1; min-width: 280px; max-width: 500px; background: #fff; border: 1px solid #cbd5e1; border-radius: 8px; padding: 0 12px; }
    .search-icon { font-size: 18px; color: #94a3b8; }
    .search-box input { width: 100%; border: 0; outline: 0; padding: 10px 0; font: inherit; font-size: 13px; color: #0f172a; }
    .clear-search { background: none; border: 0; font-size: 16px; color: #94a3b8; cursor: pointer; }

    .quick-lookup-box { display: flex; gap: 6px; background: #fff; border: 1px solid #cbd5e1; border-radius: 8px; padding: 3px 6px; }
    .quick-lookup-box input { border: 0; outline: 0; padding: 6px 8px; font: inherit; font-size: 12px; width: 160px; }
    .lookup-btn { background: #f1f5f9; border: 1px solid #cbd5e1; border-radius: 6px; padding: 6px 10px; font-size: 11px; font-weight: 700; cursor: pointer; color: #334155; }
    .lookup-btn:disabled { opacity: 0.5; cursor: not-allowed; }

    .refresh-button { border: 1px solid #cbd5e1; background: #fff; color: #334155; border-radius: 8px; padding: 8px 14px; font-size: 12px; font-weight: 600; cursor: pointer; }

    .content-card { background: #fff; border: 1px solid #e2e8f0; border-radius: 12px; box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.03); overflow: hidden; }
    .table-wrap { overflow-x: auto; }
    table { width: 100%; border-collapse: collapse; min-width: 860px; }
    th { padding: 12px 16px; background: #f8fafc; text-align: left; font-size: 11px; text-transform: uppercase; letter-spacing: 0.05em; color: #64748b; border-bottom: 1px solid #e2e8f0; font-weight: 700; }
    td { padding: 12px 16px; border-bottom: 1px solid #f1f5f9; font-size: 13px; color: #334155; vertical-align: middle; }
    tbody tr:hover { background: #f8fafc; }
    .actions-col { text-align: right; }

    .customer-name-btn { display: flex; align-items: center; gap: 10px; background: none; border: 0; padding: 0; text-align: left; cursor: pointer; }
    .name-meta { display: flex; flex-direction: column; gap: 2px; }
    .name-meta strong { font-size: 13px; color: #0f172a; }
    .name-meta small { font-size: 10px; color: #94a3b8; }
    .avatar { display: grid; place-items: center; width: 34px; height: 34px; border-radius: 8px; background: #e0e7ff; color: #4338ca; font-weight: 700; font-size: 12px; flex-shrink: 0; }
    .phone-text { font-family: ui-monospace, monospace; font-size: 12px; font-weight: 600; color: #0f172a; }
    .crm-badge { display: inline-block; padding: 2px 7px; border-radius: 4px; background: #f0fdf4; border: 1px solid #bbf7d0; color: #15803d; font-size: 11px; font-weight: 700; font-family: ui-monospace, monospace; }
    .muted-dash { color: #cbd5e1; }
    .notes-preview { display: block; max-width: 150px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; color: #64748b; font-size: 12px; }

    .row-actions { display: flex; justify-content: flex-end; gap: 6px; }
    .call-btn { border: 1px solid #86efac; background: #f0fdf4; color: #166534; border-radius: 6px; padding: 6px 10px; font-size: 11px; font-weight: 700; cursor: pointer; }
    .call-btn:hover { background: #dcfce7; }
    .action-btn { border: 1px solid #cbd5e1; background: #fff; color: #334155; border-radius: 6px; padding: 6px 9px; font-size: 11px; font-weight: 600; cursor: pointer; }
    .action-btn:hover { background: #f8fafc; }
    .delete-btn { color: #dc2626; border-color: #fca5a5; }
    .delete-btn:hover { background: #fef2f2; }

    .pagination { display: flex; align-items: center; justify-content: space-between; padding: 12px 16px; font-size: 12px; color: #64748b; border-top: 1px solid #f1f5f9; }
    .pagination-controls { display: flex; align-items: center; gap: 8px; }
    .pagination-controls button { border: 1px solid #cbd5e1; background: #fff; padding: 6px 12px; border-radius: 6px; font-size: 11px; font-weight: 600; cursor: pointer; }
    .pagination-controls button:disabled { opacity: 0.4; cursor: not-allowed; }
    .current-page-pill { padding: 4px 10px; background: #f1f5f9; border-radius: 4px; font-weight: 700; font-size: 11px; color: #0f172a; }

    .table-state { min-height: 280px; display: flex; align-items: center; justify-content: center; flex-direction: column; gap: 8px; color: #64748b; font-size: 13px; }
    .empty-icon { font-size: 32px; }
    .spinner { width: 22px; height: 22px; border: 3px solid #e2e8f0; border-top-color: #0f172a; border-radius: 50%; animation: spin .7s linear infinite; }
    @keyframes spin { to { transform: rotate(360deg); } }

    /* Overlay & Details Drawer */
    .overlay { position: fixed; inset: 0; background: rgba(15, 23, 42, 0.4); display: flex; justify-content: flex-end; z-index: 100; backdrop-filter: blur(2px); }
    .details-panel { width: min(480px, 100%); height: 100%; background: #fff; padding: 28px; box-shadow: -15px 0 40px rgba(0,0,0,0.15); overflow-y: auto; display: flex; flex-direction: column; }
    .details-header { display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 20px; }
    .details-title h3 { margin: 4px 0 0; font-size: 22px; color: #0f172a; }
    .close-btn { width: 32px; height: 32px; border: 1px solid #cbd5e1; background: #fff; border-radius: 6px; font-size: 20px; color: #64748b; cursor: pointer; display: grid; place-items: center; }

    .profile-hero { display: flex; align-items: center; gap: 14px; padding: 16px; background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 10px; margin-bottom: 16px; }
    .detail-avatar { width: 52px; height: 52px; font-size: 18px; border-radius: 10px; background: #e0e7ff; color: #4338ca; display: grid; place-items: center; font-weight: 800; flex-shrink: 0; }
    .hero-info h4 { margin: 0 0 4px; font-size: 16px; color: #0f172a; }
    .hero-info p { margin: 0 0 6px; font-size: 13px; font-family: ui-monospace, monospace; color: #475569; }

    .panel-call-button { width: 100%; background: #166534; color: #fff; border: 0; border-radius: 8px; padding: 12px; font-size: 13px; font-weight: 700; cursor: pointer; margin-bottom: 16px; }
    .panel-call-button:disabled { opacity: 0.6; cursor: not-allowed; }
    .error-inline { padding: 8px 12px; background: #fff1f2; border: 1px solid #fecdd3; border-radius: 6px; color: #9f1239; font-size: 12px; margin-bottom: 12px; }

    .drawer-tabs { display: flex; border-bottom: 1px solid #e2e8f0; margin-bottom: 16px; }
    .drawer-tabs button { background: none; border: 0; padding: 10px 14px; font-size: 13px; font-weight: 600; color: #64748b; border-bottom: 2px solid transparent; cursor: pointer; }
    .drawer-tabs button.active { color: #0f172a; border-bottom-color: #0f172a; }

    .detail-list { display: grid; gap: 14px; font-size: 13px; margin-bottom: 24px; }
    .detail-list div { padding-bottom: 10px; border-bottom: 1px solid #f1f5f9; }
    .detail-list span { display: block; font-size: 10px; text-transform: uppercase; letter-spacing: 0.05em; color: #94a3b8; margin-bottom: 4px; font-weight: 700; }
    .detail-list strong { color: #0f172a; font-size: 13px; }
    .notes-box { background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 6px; padding: 10px; font-size: 12px; color: #334155; line-height: 1.5; white-space: pre-wrap; margin-top: 4px; }

    .panel-footer-actions { display: flex; gap: 10px; margin-top: auto; padding-top: 16px; border-top: 1px solid #e2e8f0; }
    .edit-btn-large { flex: 1; padding: 10px; border: 1px solid #cbd5e1; background: #fff; border-radius: 6px; font-weight: 700; font-size: 12px; cursor: pointer; }
    .delete-btn-large { flex: 1; padding: 10px; border: 1px solid #fca5a5; background: #fef2f2; color: #b91c1c; border-radius: 6px; font-weight: 700; font-size: 12px; cursor: pointer; }

    /* Call History Timeline */
    .calls-timeline { display: flex; flex-direction: column; gap: 10px; }
    .call-card { background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; padding: 12px; }
    .call-card-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 8px; }
    .call-direction { font-size: 11px; font-weight: 700; text-transform: uppercase; padding: 2px 6px; border-radius: 4px; }
    .dir-inbound { background: #dbeafe; color: #1e40af; }
    .dir-outbound { background: #fef3c7; color: #92400e; }
    .call-status { font-size: 11px; font-weight: 600; padding: 2px 6px; border-radius: 4px; }
    .status-completed { background: #dcfce7; color: #166534; }
    .status-active { background: #f3e8ff; color: #6b21a8; }
    .status-other { background: #f1f5f9; color: #475569; }
    .call-card-meta { display: flex; flex-direction: column; gap: 3px; font-size: 12px; color: #64748b; }
    .tab-spinner, .tab-empty { padding: 30px; text-align: center; color: #64748b; font-size: 13px; }

    /* Modals */
    .modal-overlay { justify-content: center; align-items: center; padding: 20px; }
    .modal-card { background: #fff; width: min(520px, 100%); border-radius: 12px; padding: 28px; box-shadow: 0 20px 25px -5px rgba(0,0,0,0.1); max-height: 90vh; overflow-y: auto; }
    .modal-header { display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 18px; }
    .modal-header h3 { margin: 4px 0 0; font-size: 20px; color: #0f172a; }

    .form-group { margin-bottom: 14px; }
    .form-group label { display: block; font-size: 12px; font-weight: 700; color: #334155; margin-bottom: 5px; }
    .required { color: #e11d48; }
    .form-group input, .form-group textarea { width: 100%; border: 1px solid #cbd5e1; border-radius: 6px; padding: 9px 11px; font: inherit; font-size: 13px; color: #0f172a; outline: 0; box-sizing: border-box; }
    .form-group input:focus, .form-group textarea:focus { border-color: #0f172a; }
    .field-error { display: block; color: #dc2626; font-size: 11px; margin-top: 4px; }
    .form-error-banner { background: #fff1f2; border: 1px solid #fecdd3; border-radius: 6px; padding: 10px; color: #9f1239; font-size: 12px; margin-bottom: 16px; }

    .modal-actions { display: flex; justify-content: flex-end; gap: 10px; margin-top: 22px; }
    .cancel-btn { padding: 9px 16px; border: 1px solid #cbd5e1; background: #fff; border-radius: 6px; font-weight: 600; font-size: 12px; cursor: pointer; }
    .save-btn { padding: 9px 18px; background: #0f172a; color: #fff; border: 0; border-radius: 6px; font-weight: 700; font-size: 12px; cursor: pointer; }
    .save-btn:disabled { opacity: 0.5; cursor: not-allowed; }

    /* Delete Dialog */
    .delete-dialog { text-align: center; }
    .delete-icon { font-size: 38px; margin-bottom: 10px; }
    .delete-warning { font-size: 12px; color: #64748b; background: #fff7ed; border: 1px solid #ffedd5; padding: 10px; border-radius: 6px; margin: 12px 0; }
    .confirm-delete-btn { padding: 9px 18px; background: #dc2626; color: #fff; border: 0; border-radius: 6px; font-weight: 700; font-size: 12px; cursor: pointer; }
    .confirm-delete-btn:disabled { opacity: 0.5; cursor: not-allowed; }

    @media (max-width: 650px) {
      .page-header { flex-direction: column; }
      .toolbar { flex-direction: column; }
      .search-box { max-width: none; }
      .quick-lookup-box { width: 100%; }
      .quick-lookup-box input { flex: 1; width: auto; }
      .refresh-button { width: 100%; }
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CustomersComponent implements OnInit {
  private readonly customerService = inject(CustomerService);
  private readonly callService = inject(CallService);
  private readonly sessionService = inject(IncomingCallSessionService);
  private readonly realtime = inject(CallCenterRealtimeService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly destroyRef = inject(DestroyRef);

  readonly searchControl = new FormControl('', { nonNullable: true });
  readonly lookupControl = new FormControl('', { nonNullable: true });

  result: CustomerPagedResult = {
    items: [],
    page: 1,
    pageSize: 20,
    totalCount: 0,
    totalPages: 0,
    hasPreviousPage: false,
    hasNextPage: false
  };

  selectedCustomer: Customer | null = null;
  customerCalls: CustomerCallItem[] = [];
  activeTab: ActiveTab = 'profile';

  loading = false;
  callsLoading = false;
  lookupLoading = false;
  formSaving = false;
  deleteLoading = false;

  errorMessage = '';
  successMessage = '';
  callError = '';
  formError = '';
  deleteError = '';

  callingCustomerId: string | null = null;
  page = 1;
  private readonly pageSize = 20;

  // Modals state
  showFormModal = false;
  isEditing = false;
  editingCustomerId: string | null = null;
  customerToDelete: Customer | null = null;

  readonly customerForm = new FormGroup({
    fullName: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(2)] }),
    phone: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(7)] }),
    email: new FormControl('', { nonNullable: true, validators: [Validators.email] }),
    address: new FormControl('', { nonNullable: true }),
    crmCustomerId: new FormControl('', { nonNullable: true }),
    notes: new FormControl('', { nonNullable: true })
  });

  canManageCustomers(): boolean {
    return this.auth.hasRole(['Admin', 'Supervisor']);
  }

  canDeleteCustomers(): boolean {
    return this.auth.hasRole('Admin');
  }

  ngOnInit(): void {
    this.searchControl.valueChanges
      .pipe(
        startWith(''),
        debounceTime(350),
        distinctUntilChanged(),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe(() => this.loadPage(1));
  }

  loadPage(page: number): void {
    this.page = Math.max(1, page);
    this.loading = true;
    this.errorMessage = '';

    this.customerService
      .getPaged(this.searchControl.value, this.page, this.pageSize)
      .pipe(
        finalize(() => {
          this.loading = false;
          this.cdr.markForCheck();
        })
      )
      .subscribe({
        next: (result) => {
          this.result = result;
          this.cdr.markForCheck();
        },
        error: (error) => {
          this.errorMessage = error?.error?.message || 'Unable to load customer records from server.';
          this.cdr.markForCheck();
        }
      });
  }

  performPhoneLookup(): void {
    const raw = this.lookupControl.value.trim();
    if (!raw) return;

    this.lookupLoading = true;
    this.errorMessage = '';

    this.customerService
      .lookupByPhone(raw)
      .pipe(
        finalize(() => {
          this.lookupLoading = false;
          this.cdr.markForCheck();
        })
      )
      .subscribe({
        next: (customer) => {
          if (customer) {
            this.selectCustomer(customer);
            this.successMessage = `Found customer: ${customer.fullName}`;
          } else {
            this.errorMessage = `No registered customer found for phone: ${raw}`;
          }
          this.cdr.markForCheck();
        },
        error: (error) => {
          this.errorMessage = error?.error?.message || `No customer found matching phone: ${raw}`;
          this.cdr.markForCheck();
        }
      });
  }

  selectCustomer(customer: Customer): void {
    this.callError = '';
    this.activeTab = 'profile';
    this.customerCalls = [];

    this.customerService.getDetails(customer.id).subscribe({
      next: (details) => {
        this.selectedCustomer = details;
        this.cdr.markForCheck();
      },
      error: (error) => {
        this.errorMessage = error?.error?.message || 'Unable to load customer profile.';
        this.cdr.markForCheck();
      }
    });
  }

  closeDetails(): void {
    this.selectedCustomer = null;
    this.callError = '';
    this.customerCalls = [];
  }

  loadCustomerCallHistory(customerId: string): void {
    this.activeTab = 'calls';
    this.callsLoading = true;

    this.customerService
      .getCustomerCalls(customerId, 1, 20)
      .pipe(
        finalize(() => {
          this.callsLoading = false;
          this.cdr.markForCheck();
        })
      )
      .subscribe({
        next: (response) => {
          this.customerCalls = response?.items || [];
          this.cdr.markForCheck();
        },
        error: () => {
          this.customerCalls = [];
          this.cdr.markForCheck();
        }
      });
  }

  callCustomer(customer: Customer): void {
    if (this.callingCustomerId) return;
    this.callingCustomerId = customer.id;
    this.callError = '';

    this.realtime.connect();
    this.callService
      .createOutgoing({
        customerId: customer.id,
        phoneNumber: customer.phone,
        correlationId: crypto.randomUUID()
      })
      .pipe(
        finalize(() => {
          this.callingCustomerId = null;
          this.cdr.markForCheck();
        })
      )
      .subscribe({
        next: (call) => {
          this.sessionService.setSession({
            event: {
              callId: call.id,
              customerId: call.customerId,
              phoneNumber: call.phoneNumber,
              direction: this.directionLabel(call.direction),
              status: this.statusLabel(call.status),
              occurredAtUtc: call.startedAt
            },
            customer: {
              id: customer.id,
              fullName: customer.fullName,
              phone: customer.phone,
              email: customer.email,
              address: customer.address
            }
          });
          void this.router.navigate(['/active-call']);
        },
        error: (error) => {
          this.callError = error?.error?.message || 'Unable to initiate outgoing call to customer.';
          this.cdr.markForCheck();
        }
      });
  }

  // Create / Edit Modal logic
  openCreateModal(): void {
    this.isEditing = false;
    this.editingCustomerId = null;
    this.formError = '';
    this.customerForm.reset({
      fullName: '',
      phone: '',
      email: '',
      address: '',
      crmCustomerId: '',
      notes: ''
    });
    this.showFormModal = true;
  }

  openEditModal(customer: Customer): void {
    this.isEditing = true;
    this.editingCustomerId = customer.id;
    this.formError = '';
    this.customerForm.setValue({
      fullName: customer.fullName,
      phone: customer.phone,
      email: customer.email || '',
      address: customer.address || '',
      crmCustomerId: customer.crmCustomerId || '',
      notes: customer.notes || ''
    });
    this.showFormModal = true;
  }

  closeFormModal(): void {
    this.showFormModal = false;
    this.isEditing = false;
    this.editingCustomerId = null;
    this.formError = '';
  }

  submitForm(): void {
    if (this.customerForm.invalid) {
      this.customerForm.markAllAsTouched();
      return;
    }

    this.formSaving = true;
    this.formError = '';
    const val = this.customerForm.getRawValue();

    if (this.isEditing && this.editingCustomerId) {
      this.customerService
        .update(this.editingCustomerId, {
          fullName: val.fullName,
          phone: val.phone,
          email: val.email || null,
          address: val.address || null,
          crmCustomerId: val.crmCustomerId || null,
          notes: val.notes || null
        })
        .pipe(
          finalize(() => {
            this.formSaving = false;
            this.cdr.markForCheck();
          })
        )
        .subscribe({
          next: (updated) => {
            this.successMessage = `Customer "${updated.fullName}" updated successfully.`;
            if (this.selectedCustomer?.id === updated.id) {
              this.selectedCustomer = updated;
            }
            this.closeFormModal();
            this.loadPage(this.page);
          },
          error: (error) => {
            this.formError =
              error?.status === 409
                ? 'Duplicate Phone: A customer with this phone number already exists.'
                : error?.error?.message || 'Failed to update customer.';
            this.cdr.markForCheck();
          }
        });
    } else {
      this.customerService
        .create({
          fullName: val.fullName,
          phone: val.phone,
          email: val.email || null,
          address: val.address || null,
          crmCustomerId: val.crmCustomerId || null,
          notes: val.notes || null
        })
        .pipe(
          finalize(() => {
            this.formSaving = false;
            this.cdr.markForCheck();
          })
        )
        .subscribe({
          next: (created) => {
            this.successMessage = `Customer "${created.fullName}" created successfully.`;
            this.closeFormModal();
            this.loadPage(1);
          },
          error: (error) => {
            this.formError =
              error?.status === 409
                ? 'Duplicate Phone: A customer with this phone number already exists.'
                : error?.error?.message || 'Failed to create customer.';
            this.cdr.markForCheck();
          }
        });
    }
  }

  // Delete Confirmation Logic
  confirmDelete(customer: Customer): void {
    this.customerToDelete = customer;
    this.deleteError = '';
  }

  cancelDelete(): void {
    this.customerToDelete = null;
    this.deleteError = '';
  }

  executeDelete(): void {
    if (!this.customerToDelete) return;
    const target = this.customerToDelete;

    this.deleteLoading = true;
    this.deleteError = '';

    this.customerService
      .delete(target.id)
      .pipe(
        finalize(() => {
          this.deleteLoading = false;
          this.cdr.markForCheck();
        })
      )
      .subscribe({
        next: () => {
          this.successMessage = `Customer "${target.fullName}" deleted successfully.`;
          if (this.selectedCustomer?.id === target.id) {
            this.closeDetails();
          }
          this.cancelDelete();
          this.loadPage(this.page);
        },
        error: (error) => {
          this.deleteError =
            error?.status === 409
              ? 'Cannot Delete: This customer has linked call records and cannot be deleted.'
              : error?.error?.message || 'Failed to delete customer.';
          this.cdr.markForCheck();
        }
      });
  }

  // Utilities
  initials(name: string): string {
    return name
      .split(' ')
      .filter(Boolean)
      .slice(0, 2)
      .map((x) => x[0])
      .join('')
      .toUpperCase();
  }

  statusLabel(status: CallStatus | string | number): string {
    return typeof status === 'number' ? (CallStatus[status] ?? 'Unknown') : String(status);
  }

  statusClass(status: CallStatus | string | number): string {
    const s = String(status).toLowerCase();
    if (s.includes('completed')) return 'completed';
    if (s.includes('active') || s.includes('connected') || s.includes('ringing')) return 'active';
    return 'other';
  }

  directionLabel(direction: CallDirection | string | number): string {
    return typeof direction === 'number' ? (CallDirection[direction] ?? 'Outbound') : String(direction);
  }

  directionClass(direction: CallDirection | string | number): string {
    const d = String(direction).toLowerCase();
    return d.includes('in') ? 'inbound' : 'outbound';
  }
}
