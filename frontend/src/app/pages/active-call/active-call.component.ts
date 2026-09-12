import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  DestroyRef,
  inject,
  OnInit
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { interval, finalize, of, switchMap } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CallCenterRealtimeService } from '../../core/services/call-center-realtime.service';
import {
  IncomingCallSessionService,
  ActiveCallSession
} from '../../core/services/incoming-call-session.service';
import { TelephonyService } from '../../core/services/telephony.service';
import { TwilioVoiceService } from '../../core/services/twilio-voice.service';
import { CallService, CallResponse } from '../../core/services/call.service';
import { CustomerService } from '../../core/services/customer.service';
import { AgentService } from '../../core/services/agent.service';
import { QueueService } from '../../core/services/queue.service';
import { AgentDashboardService } from '../../core/services/agent-dashboard.service';
import {
  AgentDashboard,
  CallStatus,
  CallDirection
} from '../../core/models/agent-dashboard.models';
import {
  CallTimelineEvent,
  CallTransferredEvent,
  EligibleAgent,
  TransferCallRequest,
  TransferType
} from '../../core/models/incoming-call.models';
import { Customer, CustomerCallItem } from '../../core/models/customer.models';
import { Agent } from '../../core/models/agent.models';
import { CallQueue } from '../../core/models/queue.models';
import { DispositionService } from '../../core/services/disposition.service';
import { CallDisposition, CompleteCallRequest } from '../../core/models/disposition.models';

@Component({
  selector: 'app-active-call',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <div class="active-page">
      <!-- Page Header -->
      <div class="page-header">
        <div>
          <span class="eyebrow">Telephony Operations</span>
          <h2>Active Call Workspace</h2>
          <p>Real-time conversational control, customer context, notes, and call event history.</p>
        </div>
        <div class="header-badges">
          <span class="signalr-pill">
            <span class="pulse-indicator"></span>
            LIVE REALTIME
          </span>
          <span class="softphone-pill" [class.registered]="(voiceService.deviceState$ | async) === 'registered'">
            🎧 {{ (voiceService.currentProvider$ | async) }} WebRTC: {{ (voiceService.deviceState$ | async) | uppercase }}
          </span>
          @if (isMuted) {
            <span class="status-chip" style="background: #fef2f2; color: #dc2626; border: 1px solid #fecaca; font-weight: 700;">🔇 MIC MUTED</span>
          }
          @if (currentStatus === CallStatus.OnHold) {
            <span class="status-chip hold-chip">CALL ON HOLD</span>
          } @else if (currentStatus === CallStatus.Connected) {
            <span class="status-chip connected-chip">CALL CONNECTED</span>
          } @else if (currentStatus === CallStatus.Ringing) {
            <span class="status-chip ringing-chip">RINGING</span>
          } @else if (isTerminal) {
            <span class="status-chip terminal-chip">{{ statusLabel(currentStatus) }}</span>
          }
        </div>
      </div>

      <!-- Error / Action Messages -->
      @if (errorMessage) {
        <div class="alert-banner error-banner">
          <span class="alert-icon">⚠️</span>
          <span>{{ errorMessage }}</span>
          <button type="button" class="close-alert" (click)="errorMessage = ''">×</button>
        </div>
      }
      @if (actionSuccessMessage) {
        <div class="alert-banner success-banner">
          <span class="alert-icon">✅</span>
          <span>{{ actionSuccessMessage }}</span>
          <button type="button" class="close-alert" (click)="actionSuccessMessage = ''">×</button>
        </div>
      }

      <!-- Empty State -->
      @if (!session && !loadingSession) {
        <div class="empty-state-card">
          <div class="empty-icon">📞</div>
          <h3>No Active Call Found</h3>
          <p>You do not have a connected or ringing call currently assigned to your agent console.</p>
          <div class="empty-actions">
            <a routerLink="/incoming-call" class="primary-btn">Incoming Calls Console</a>
            <a routerLink="/agent-dashboard" class="secondary-btn">My Agent Dashboard</a>
          </div>
        </div>
      } @else if (loadingSession) {
        <div class="loading-container">
          <div class="spinner"></div>
          <span>Loading active call workspace…</span>
        </div>
      } @else if (session) {
        <!-- Main Workspace Grid -->
        <div class="workspace-grid">
          <!-- Left Column: Context & Controls -->
          <div class="main-column">
            <!-- Caller & Call Identity Card -->
            <section class="panel-card caller-card">
              <div class="caller-primary">
                <div class="avatar" [class.avatar-hold]="currentStatus === CallStatus.OnHold">
                  {{ initials(customer?.fullName || session.customer?.fullName || 'Customer') }}
                </div>
                <div class="caller-details">
                  <div class="caller-title-row">
                    <h3>{{ customer?.fullName || session.customer?.fullName || 'Unknown Caller' }}</h3>
                    <span class="badge" [ngClass]="statusBadgeClass(currentStatus)">
                      {{ statusLabel(currentStatus) }}
                    </span>
                  </div>
                  <div class="caller-contact-row">
                    <span class="contact-pill phone-pill">
                      📞 {{ session.customer?.phone || session.event.phoneNumber }}
                    </span>
                    @if (customer?.crmCustomerId) {
                      <span class="contact-pill crm-pill">CRM ID: {{ customer?.crmCustomerId }}</span>
                    }
                    @if (customer?.email || session.customer?.email) {
                      <span class="contact-pill email-pill">
                        ✉️ {{ customer?.email || session.customer?.email }}
                      </span>
                    }
                  </div>
                  @if (customer?.address || session.customer?.address) {
                    <div class="caller-address">
                      📍 {{ customer?.address || session.customer?.address }}
                    </div>
                  }
                </div>
              </div>

              <!-- Metadata Grid -->
              <div class="meta-grid">
                <div class="meta-item">
                  <span class="meta-label">Call Duration</span>
                  <strong class="meta-value duration-value" [class.duration-paused]="currentStatus === CallStatus.OnHold">
                    {{ formattedDuration }}
                  </strong>
                </div>
                <div class="meta-item">
                  <span class="meta-label">Call ID</span>
                  <strong class="meta-value code-value" [title]="session.event.callId">
                    {{ session.event.callId.slice(0, 8) }}…{{ session.event.callId.slice(-4) }}
                  </strong>
                </div>
                <div class="meta-item">
                  <span class="meta-label">Assigned Agent</span>
                  <strong class="meta-value">
                    {{ agent?.displayName || 'Loading…' }} ({{ agent?.employeeCode || '—' }})
                  </strong>
                </div>
                <div class="meta-item">
                  <span class="meta-label">Agent Team</span>
                  <strong class="meta-value">{{ agent?.team || 'General Team' }}</strong>
                </div>
                <div class="meta-item">
                  <span class="meta-label">Direction</span>
                  <strong class="meta-value">{{ session.event.direction || 'Inbound' }}</strong>
                </div>
                <div class="meta-item">
                  <span class="meta-label">Started At</span>
                  <strong class="meta-value">{{ callStartTimeFormatted }}</strong>
                </div>
              </div>

              <!-- Action Controls Bar -->
              <div class="controls-bar">
                <div class="controls-status-info">
                  @if (currentStatus === CallStatus.OnHold) {
                    <span class="hold-indicator">⏸️ Call is currently On Hold. Customer is listening to hold music.</span>
                  } @else if (currentStatus === CallStatus.Connected) {
                    <span class="connected-indicator">🟢 Line active and streaming audio.</span>
                  } @else if (currentStatus === CallStatus.Ringing) {
                    <span class="ringing-indicator">🔔 Call is currently ringing.</span>
                  } @else {
                    <span class="terminal-indicator">Call has concluded ({{ statusLabel(currentStatus) }}).</span>
                  }
                </div>

                <div class="actions-group">
                  <!-- Ringing state controls -->
                  @if (currentStatus === CallStatus.Ringing) {
                    <button
                      type="button"
                      class="btn btn-accept"
                      [disabled]="processing"
                      (click)="acceptCall()">
                      📞 Accept Call
                    </button>
                    <button
                      type="button"
                      class="btn btn-reject"
                      [disabled]="processing"
                      (click)="rejectCall()">
                      🚫 Reject Call
                    </button>
                  }

                  <!-- Connected & OnHold state controls -->
                  @if (currentStatus === CallStatus.Connected || currentStatus === CallStatus.OnHold) {
                    <button
                      type="button"
                      class="btn"
                      [class.btn-muted]="isMuted"
                      [class.btn-unmuted]="!isMuted"
                      [disabled]="processing || isTerminal || currentStatus === CallStatus.OnHold"
                      (click)="toggleMute()">
                      {{ isMuted ? '🎙️ Unmute' : '🔇 Mute' }}
                    </button>
                  }

                  @if (currentStatus === CallStatus.Connected) {
                    <button
                      type="button"
                      class="btn btn-hold"
                      [disabled]="processing || isTerminal"
                      (click)="holdCall()">
                      ⏸️ Put on Hold
                    </button>
                  } @else if (currentStatus === CallStatus.OnHold) {
                    <button
                      type="button"
                      class="btn btn-resume"
                      [disabled]="processing || isTerminal"
                      (click)="resumeCall()">
                      ▶️ Resume Call
                    </button>
                  }

                  @if (currentStatus === CallStatus.Connected || currentStatus === CallStatus.OnHold) {
                    <button
                      type="button"
                      class="btn btn-transfer"
                      [disabled]="processing || isTerminal"
                      (click)="openTransferModal()">
                      🔀 Transfer Call
                    </button>

                    <button
                      type="button"
                      class="btn btn-end"
                      [disabled]="processing || isTerminal"
                      (click)="openDispositionModal()">
                      📋 Wrap-Up & End Call
                    </button>
                  }

                  @if (isTerminal) {
                    <button
                      type="button"
                      class="btn btn-primary"
                      (click)="navigateToDashboard()">
                      Return to Dashboard
                    </button>
                  }
                </div>
              </div>
            </section>

            <!-- Call Notes Section -->
            <section class="panel-card notes-card">
              <div class="panel-heading">
                <div>
                  <h4>Call Wrap-up & Agent Notes</h4>
                  <p>Document caller requirements, resolution details, or follow-up items.</p>
                </div>
                @if (notesSavedAt) {
                  <span class="save-status-tag">Saved {{ notesSavedAt | date:'mediumTime' }}</span>
                }
              </div>

              <div class="notes-body">
                <textarea
                  class="notes-textarea"
                  rows="4"
                  [(ngModel)]="notes"
                  placeholder="Enter notes about this conversation (e.g., customer ordered Falaq Biryani with extra sauce, delivery scheduled for 7:30 PM)..."
                  [disabled]="processing && activeAction === 'notes'">
                </textarea>
                <div class="notes-footer">
                  <span class="char-count">{{ notes.length }} characters</span>
                  <button
                    type="button"
                    class="btn btn-save-notes"
                    [disabled]="(processing && activeAction === 'notes') || !notes.trim()"
                    (click)="saveNotes()">
                    {{ (processing && activeAction === 'notes') ? 'Saving…' : 'Save Notes' }}
                  </button>
                </div>
              </div>
            </section>

            <!-- Customer Past Call History Section -->
            <section class="panel-card customer-history-card">
              <div class="panel-heading">
                <div>
                  <h4>Customer Past Call History</h4>
                  <p>Recent interactions recorded for this customer.</p>
                </div>
                <span class="history-count">{{ customerCalls.length }} recorded</span>
              </div>

              @if (customerCallsLoading) {
                <div class="history-loading">
                  <div class="mini-spinner"></div>
                  <span>Fetching caller records…</span>
                </div>
              } @else if (!session.customer?.id && !session.event.customerId) {
                <div class="history-empty">
                  <span>Unregistered caller — no prior call records available for this phone number.</span>
                </div>
              } @else if (customerCalls.length === 0) {
                <div class="history-empty">
                  <span>No prior call records found for this customer. This is their first recorded call.</span>
                </div>
              } @else {
                <div class="history-table-wrapper">
                  <table class="history-table">
                    <thead>
                      <tr>
                        <th>Date & Time</th>
                        <th>Direction</th>
                        <th>Status</th>
                        <th>Duration</th>
                        <th>Phone</th>
                      </tr>
                    </thead>
                    <tbody>
                      @for (call of customerCalls; track call.id) {
                        <tr>
                          <td>{{ call.startedAt | date:'MMM d, y, h:mm a' }}</td>
                          <td>
                            <span class="mini-pill" [class.mini-inbound]="isDirectionInbound(call.direction)">
                              {{ formatDirection(call.direction) }}
                            </span>
                          </td>
                          <td>
                            <span class="mini-status" [class.status-completed]="isCallCompleted(call.status)">
                              {{ formatCallStatus(call.status) }}
                            </span>
                          </td>
                          <td class="duration-cell">{{ formatSeconds(call.durationSeconds) }}</td>
                          <td>{{ call.phoneNumber }}</td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </div>
              }
            </section>
          </div>

          <!-- Right Column: Live Call Timeline Feed -->
          <div class="side-column">
            <section class="panel-card timeline-card">
              <div class="panel-heading">
                <div>
                  <h4>Call Event Timeline</h4>
                  <p>Chronological audit trail</p>
                </div>
                <button
                  type="button"
                  class="btn-icon"
                  title="Refresh Timeline"
                  (click)="loadTimeline()">
                  🔄
                </button>
              </div>

              @if (timelineLoading) {
                <div class="timeline-loading">
                  <div class="mini-spinner"></div>
                  <span>Loading timeline…</span>
                </div>
              } @else if (timeline.length === 0) {
                <div class="timeline-empty">
                  <span>No timeline events recorded yet.</span>
                </div>
              } @else {
                <div class="timeline-feed">
                  @for (event of timeline; track event.id; let last = $last) {
                    <div class="timeline-item" [class.timeline-item-last]="last">
                      <div class="timeline-icon-col">
                        <div class="timeline-icon" [ngClass]="timelineIconClass(event.eventType)">
                          {{ timelineIconEmoji(event.eventType) }}
                        </div>
                        @if (!last) {
                          <div class="timeline-line"></div>
                        }
                      </div>
                      <div class="timeline-content">
                        <div class="timeline-header">
                          <strong class="timeline-event-name">{{ event.eventType }}</strong>
                          <span class="timeline-time">{{ event.occurredAt | date:'shortTime' }}</span>
                        </div>
                        <p class="timeline-desc">{{ event.description }}</p>
                        @if (event.agentName) {
                          <span class="timeline-agent">👤 {{ event.agentName }}</span>
                        }
                      </div>
                    </div>
                  }
                </div>
              }
            </section>
          </div>
        </div>
      }

      <!-- Transfer Modal -->
      @if (transferModalOpen) {
        <div class="modal-backdrop" (click)="closeTransferModal()">
          <div class="modal-card" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <div>
                <h3>Transfer Active Call</h3>
                <p>Enterprise Call Routing • Cold, Consultative, and Escalation Handover</p>
              </div>
              <button type="button" class="modal-close" (click)="closeTransferModal()">×</button>
            </div>

            <div class="modal-body">
              <!-- Transfer Strategy Selector -->
              <div class="transfer-tabs">
                <button
                  type="button"
                  class="tab-btn"
                  [class.active-tab]="transferMode === 'agent' && transferStrategy === TransferType.Blind"
                  (click)="setTransferStrategy(TransferType.Blind)">
                  ⚡ Blind Transfer
                </button>
                <button
                  type="button"
                  class="tab-btn"
                  [class.active-tab]="transferMode === 'agent' && transferStrategy === TransferType.Warm"
                  (click)="setTransferStrategy(TransferType.Warm)">
                  🤝 Warm Transfer
                </button>
                <button
                  type="button"
                  class="tab-btn"
                  [class.active-tab]="transferMode === 'agent' && transferStrategy === TransferType.Supervisor"
                  (click)="setTransferStrategy(TransferType.Supervisor)">
                  🛡️ Supervisor
                </button>
                <button
                  type="button"
                  class="tab-btn"
                  [class.active-tab]="transferMode === 'queue'"
                  (click)="setTransferModeQueue()">
                  📋 Call Queue
                </button>
              </div>

              <!-- Strategy Explanation Banner -->
              <div class="transfer-info-banner" [ngClass]="getStrategyBannerClass()">
                <span class="info-icon">{{ getStrategyBannerIcon() }}</span>
                <div class="info-text">
                  <strong>{{ getStrategyBannerTitle() }}</strong>
                  <p>{{ getStrategyBannerDesc() }}</p>
                </div>
              </div>

              <!-- Mode: Agent -->
              @if (transferMode === 'agent') {
                <div class="form-group">
                  <div class="label-row">
                    <label>Select Target {{ transferStrategy === TransferType.Supervisor ? 'Supervisor / Admin' : 'Agent' }} <span class="required">*</span></label>
                    <span class="badge-count">{{ eligibleAgents.length }} Available</span>
                  </div>
                  @if (loadingTransferTargets) {
                    <div class="select-loading">
                      <div class="mini-spinner"></div>
                      <span>Querying eligible online agents…</span>
                    </div>
                  } @else if (eligibleAgents.length === 0) {
                    <div class="select-empty-warning">
                      ⚠️ No eligible {{ transferStrategy === TransferType.Supervisor ? 'supervisors or administrators' : 'agents' }} currently available for transfer.
                    </div>
                  } @else {
                    <select
                      class="form-select"
                      [(ngModel)]="selectedTargetAgentId"
                      (ngModelChange)="onAgentSelected($event)">
                      <option value="">-- Choose an Eligible {{ transferStrategy === TransferType.Supervisor ? 'Supervisor' : 'Agent' }} --</option>
                      @for (target of eligibleAgents; track target.agentId) {
                        <option [value]="target.agentId">
                          {{ target.displayName }} ({{ target.employeeCode }}) — {{ target.roleName }} • {{ target.team || 'General' }}
                        </option>
                      }
                    </select>
                  }
                </div>

                @if (selectedTargetAgent) {
                  <div class="target-preview-card">
                    <div class="preview-avatar">👤</div>
                    <div class="preview-details">
                      <div class="preview-title">
                        <span class="preview-name">{{ selectedTargetAgent.displayName }}</span>
                        <span class="role-pill" [class.supervisor-pill]="selectedTargetAgent.roleName === 'Supervisor' || selectedTargetAgent.roleName === 'Admin'">
                          {{ selectedTargetAgent.roleName }}
                        </span>
                      </div>
                      <div class="preview-sub">
                        <span>ID: <strong>{{ selectedTargetAgent.employeeCode }}</strong></span>
                        <span>•</span>
                        <span>Team: <strong>{{ selectedTargetAgent.team || 'General' }}</strong></span>
                        <span>•</span>
                        <span class="status-online">🟢 {{ selectedTargetAgent.status }}</span>
                      </div>
                    </div>
                  </div>
                }
              }

              <!-- Mode: Queue -->
              @if (transferMode === 'queue') {
                <div class="form-group">
                  <label>Select Target Queue <span class="required">*</span></label>
                  @if (loadingTransferTargets) {
                    <div class="select-loading">
                      <div class="mini-spinner"></div>
                      <span>Loading call queues…</span>
                    </div>
                  } @else if (queues.length === 0) {
                    <div class="select-empty-warning">
                      ⚠️ No active call queues found in the system.
                    </div>
                  } @else {
                    <select class="form-select" [(ngModel)]="selectedTargetQueueId">
                      <option value="">-- Choose a Queue --</option>
                      @for (q of queues; track q.id) {
                        <option [value]="q.id">
                          {{ q.name }} (Priority: {{ q.priority }})
                        </option>
                      }
                    </select>
                  }
                </div>
              }

              <!-- Quick Reasons & Reason Input -->
              <div class="form-group">
                <label>Transfer Reason</label>
                <div class="chips-row">
                  <button type="button" class="chip-btn" (click)="applyReasonChip('Supervisor Escalation')">🛡️ Escalation</button>
                  <button type="button" class="chip-btn" (click)="applyReasonChip('Customer Requested Transfer')">👤 Customer Request</button>
                  <button type="button" class="chip-btn" (click)="applyReasonChip('Order Inquiry / Support')">📦 Order Support</button>
                  <button type="button" class="chip-btn" (click)="applyReasonChip('Language / Bengali Assistance')">🌐 Language Support</button>
                </div>
                <input
                  type="text"
                  class="form-input"
                  [(ngModel)]="transferReason"
                  placeholder="e.g., Escalation to supervisor, Bengali support required"
                  maxlength="200" />
              </div>

              <!-- Confirmation Summary Box -->
              @if (canSubmitTransfer) {
                <div class="confirmation-box">
                  <div class="confirm-header">
                    <span>📌 Transfer Confirmation Summary</span>
                  </div>
                  <div class="confirm-body">
                    <div class="summary-line">
                      <span class="label">Method:</span>
                      <strong class="value">{{ transferMode === 'queue' ? 'Queue Reassignment' : getTransferStrategyLabel(transferStrategy) }}</strong>
                    </div>
                    <div class="summary-line">
                      <span class="label">Destination:</span>
                      <strong class="value highlight">
                        {{ transferMode === 'queue' ? selectedQueueName : (selectedTargetAgent?.displayName + ' (' + selectedTargetAgent?.roleName + ')') }}
                      </strong>
                    </div>
                    @if (transferReason) {
                      <div class="summary-line">
                        <span class="label">Reason:</span>
                        <span class="value">{{ transferReason }}</span>
                      </div>
                    }
                  </div>
                </div>
              }
            </div>

            <div class="modal-footer">
              <button
                type="button"
                class="secondary-btn"
                [disabled]="transferSubmitting"
                (click)="closeTransferModal()">
                Cancel
              </button>
              <button
                type="button"
                class="primary-btn transfer-confirm-btn"
                [disabled]="transferSubmitting || !canSubmitTransfer"
                (click)="submitTransfer()">
                {{ transferSubmitting ? 'Transferring…' : 'Execute Transfer' }}
              </button>
            </div>
          </div>
        </div>
      }

      <!-- Call Disposition & Wrap-Up Modal -->
      @if (dispositionModalOpen) {
        <div class="modal-backdrop" (click)="!isTerminal && closeDispositionModal()">
          <div class="modal-card disposition-modal" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <div>
                <span class="modal-eyebrow">Call Wrap-Up</span>
                <h3>Call Outcome & Disposition</h3>
              </div>
              @if (!isTerminal) {
                <button type="button" class="modal-close" (click)="closeDispositionModal()">×</button>
              }
            </div>

            <div class="modal-body">
              <p class="modal-lead">
                Select a business disposition outcome to conclude this call with
                <strong>{{ customer?.fullName || session?.event?.phoneNumber || 'the customer' }}</strong>.
              </p>

              @if (loadingDispositions) {
                <div class="loading-state">
                  <div class="spinner"></div>
                  <span>Loading available disposition outcomes…</span>
                </div>
              } @else if (dispositionLoadError) {
                <div class="select-empty-warning" style="margin-bottom: 14px;">
                  <span>{{ dispositionLoadError }}</span>
                  <button type="button" class="chip-btn" style="margin-left: 8px;" (click)="loadDispositions()">Retry</button>
                </div>
              } @else {
                <label class="form-group-label" style="font-weight: 700; font-size: 13px; color: #1e293b; margin-bottom: 8px; display: block;">
                  Select Outcome <span class="required">*</span>
                </label>
                <div class="disposition-grid">
                  @for (disp of availableDispositions; track disp.id) {
                    <div
                      class="disposition-card"
                      [class.selected]="selectedDispositionId === disp.id"
                      (click)="selectDisposition(disp)">
                      <div class="disp-top">
                        <span class="disp-icon">{{ getDispositionIcon(disp.code) }}</span>
                        <span class="disp-name">{{ disp.name }}</span>
                      </div>
                      @if (disp.description) {
                        <p class="disp-desc">{{ disp.description }}</p>
                      }
                      <div class="disp-badges">
                        @if (disp.requiresFollowUp) {
                          <span class="disp-badge badge-warn">⏰ Follow-up</span>
                        }
                        @if (disp.requiresNotes) {
                          <span class="disp-badge badge-notes">📝 Notes req.</span>
                        }
                      </div>
                    </div>
                  }
                </div>

                @if (selectedDisposition?.requiresFollowUp) {
                  <div class="followup-panel">
                    <div class="panel-tag">⏰ Follow-Up Scheduling (Mandatory)</div>
                    <div class="form-group" style="margin-bottom: 10px;">
                      <label>Scheduled Date & Time <span class="required">*</span></label>
                      <input
                        type="datetime-local"
                        class="form-input"
                        [(ngModel)]="followUpDateTime"
                        [min]="minFollowUpDateTime" />
                      <span class="hint" style="font-size: 11.5px; color: #64748b; margin-top: 4px; display: block;">Select a future date and time for agent follow-up call.</span>
                    </div>
                    <div class="form-group" style="margin-bottom: 0;">
                      <label>Follow-Up Instructions / Reason</label>
                      <input
                        type="text"
                        class="form-input"
                        placeholder="e.g. Call back customer with revised order or menu details"
                        [(ngModel)]="followUpNotes" />
                    </div>
                  </div>
                }

                <div class="form-group wrapup-notes-panel">
                  <label>
                    Wrap-Up Notes
                    @if (selectedDisposition?.requiresNotes) {
                      <span class="required">* (Mandatory for {{ selectedDisposition?.name }})</span>
                    } @else {
                      <span style="font-weight: normal; color: #64748b;">(Optional)</span>
                    }
                  </label>
                  <textarea
                    class="form-input textarea"
                    rows="3"
                    placeholder="Enter call outcome details or key takeaways..."
                    [(ngModel)]="wrapUpNotes"></textarea>
                </div>
              }

              @if (wrapUpErrorMessage) {
                <div class="alert-banner" style="background: #fef2f2; border: 1px solid #fecaca; color: #dc2626; padding: 10px 14px; border-radius: 8px; margin-top: 12px; font-size: 13px;">
                  ⚠️ {{ wrapUpErrorMessage }}
                </div>
              }
            </div>

            <div class="modal-footer">
              @if (!isTerminal) {
                <button
                  type="button"
                  class="secondary-btn"
                  [disabled]="completingCall"
                  (click)="closeDispositionModal()">
                  Cancel
                </button>
              }
              <button
                type="button"
                class="primary-btn complete-confirm-btn"
                [disabled]="completingCall || !canSubmitDisposition"
                (click)="submitCallCompletion()">
                {{ completingCall ? 'Completing Call…' : 'Complete & Save Outcome' }}
              </button>
            </div>
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    .active-page {
      max-width: 1240px;
      margin: 0 auto;
      padding-bottom: 40px;
    }

    /* Page Header */
    .page-header {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: 20px;
      margin-bottom: 20px;
    }
    .page-header h2 {
      font-size: 24px;
      font-weight: 800;
      color: #0f172a;
      margin: 2px 0 4px;
    }
    .page-header p {
      color: #64748b;
      font-size: 13.5px;
      margin: 0;
    }
    .eyebrow {
      font-size: 11px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.08em;
      color: #0284c7;
    }
    .header-badges {
      display: flex;
      align-items: center;
      gap: 10px;
      flex-wrap: wrap;
    }
    .signalr-pill {
      display: flex;
      align-items: center;
      gap: 7px;
      padding: 6px 12px;
      border-radius: 999px;
      background: #f0fdf4;
      border: 1px solid #bbf7d0;
      color: #166534;
      font-size: 11px;
      font-weight: 800;
      letter-spacing: 0.04em;
    }
    .softphone-pill {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 6px 12px;
      border-radius: 999px;
      font-size: 11px;
      font-weight: 700;
      letter-spacing: 0.04em;
      background: #f1f5f9;
      color: #475569;
      border: 1px solid #cbd5e1;
    }
    .softphone-pill.registered {
      background: #ecfdf5;
      color: #047857;
      border-color: #a7f3d0;
    }
    .pulse-indicator {
      width: 8px;
      height: 8px;
      border-radius: 50%;
      background: #16a34a;
      box-shadow: 0 0 0 4px rgba(34, 197, 94, 0.25);
    }
    .status-chip {
      padding: 6px 12px;
      border-radius: 999px;
      font-size: 11px;
      font-weight: 800;
      letter-spacing: 0.04em;
    }
    .connected-chip {
      background: #ecfdf5;
      color: #047857;
      border: 1px solid #a7f3d0;
    }
    .hold-chip {
      background: #fffbeb;
      color: #b45309;
      border: 1px solid #fde68a;
    }
    .ringing-chip {
      background: #eff6ff;
      color: #1d4ed8;
      border: 1px solid #bfdbfe;
    }
    .terminal-chip {
      background: #f1f5f9;
      color: #475569;
      border: 1px solid #cbd5e1;
    }

    /* Banners */
    .alert-banner {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 12px 16px;
      border-radius: 10px;
      margin-bottom: 20px;
      font-size: 13px;
    }
    .error-banner {
      background: #fef2f2;
      border: 1px solid #fecaca;
      color: #991b1b;
    }
    .success-banner {
      background: #f0fdf4;
      border: 1px solid #bbf7d0;
      color: #166534;
    }
    .close-alert {
      margin-left: auto;
      background: none;
      border: none;
      font-size: 18px;
      cursor: pointer;
      color: inherit;
      line-height: 1;
    }

    /* Workspace Grid */
    .workspace-grid {
      display: grid;
      grid-template-columns: 1fr 380px;
      gap: 22px;
      align-items: start;
    }

    /* Panel Card Base */
    .panel-card {
      background: #fff;
      border: 1px solid #e2e8f0;
      border-radius: 16px;
      box-shadow: 0 4px 20px rgba(15, 23, 42, 0.04);
      padding: 24px;
      margin-bottom: 22px;
    }
    .panel-heading {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      margin-bottom: 16px;
    }
    .panel-heading h4 {
      font-size: 16px;
      font-weight: 750;
      color: #0f172a;
      margin: 0 0 3px;
    }
    .panel-heading p {
      font-size: 12.5px;
      color: #64748b;
      margin: 0;
    }

    /* Caller Card */
    .caller-primary {
      display: flex;
      align-items: flex-start;
      gap: 18px;
    }
    .avatar {
      width: 60px;
      height: 60px;
      border-radius: 16px;
      background: #f0fdf4;
      color: #15803d;
      border: 2px solid #bbf7d0;
      display: grid;
      place-items: center;
      font-size: 18px;
      font-weight: 800;
      flex-shrink: 0;
    }
    .avatar-hold {
      background: #fffbeb;
      color: #b45309;
      border-color: #fde68a;
    }
    .caller-details {
      flex: 1;
    }
    .caller-title-row {
      display: flex;
      align-items: center;
      gap: 12px;
      margin-bottom: 6px;
    }
    .caller-title-row h3 {
      font-size: 22px;
      font-weight: 800;
      color: #0f172a;
      margin: 0;
    }
    .badge {
      padding: 4px 9px;
      border-radius: 6px;
      font-size: 11px;
      font-weight: 750;
      text-transform: uppercase;
    }
    .badge-connected {
      background: #ecfdf5;
      color: #047857;
      border: 1px solid #a7f3d0;
    }
    .badge-hold {
      background: #fffbeb;
      color: #b45309;
      border: 1px solid #fde68a;
    }
    .badge-ringing {
      background: #eff6ff;
      color: #1d4ed8;
      border: 1px solid #bfdbfe;
    }
    .badge-terminal {
      background: #f1f5f9;
      color: #475569;
      border: 1px solid #cbd5e1;
    }
    .caller-contact-row {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
      margin-bottom: 6px;
    }
    .contact-pill {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      padding: 3px 8px;
      border-radius: 6px;
      font-size: 12px;
      font-weight: 600;
    }
    .phone-pill {
      background: #f1f5f9;
      color: #0f172a;
      border: 1px solid #e2e8f0;
    }
    .crm-pill {
      background: #eff6ff;
      color: #1d4ed8;
      border: 1px solid #bfdbfe;
    }
    .email-pill {
      background: #f8fafc;
      color: #475569;
      border: 1px solid #e2e8f0;
    }
    .caller-address {
      font-size: 12px;
      color: #64748b;
      margin-top: 4px;
    }

    /* Metadata Grid */
    .meta-grid {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 16px;
      margin-top: 22px;
      padding: 18px 0;
      border-top: 1px solid #f1f5f9;
      border-bottom: 1px solid #f1f5f9;
    }
    .meta-item {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }
    .meta-label {
      font-size: 11px;
      text-transform: uppercase;
      letter-spacing: 0.06em;
      color: #94a3b8;
      font-weight: 700;
    }
    .meta-value {
      font-size: 13.5px;
      color: #1e293b;
      font-weight: 650;
    }
    .duration-value {
      font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
      font-size: 22px;
      font-weight: 800;
      color: #0f172a;
    }
    .duration-paused {
      color: #b45309;
      animation: blink 1.2s infinite;
    }
    @keyframes blink {
      0%, 100% { opacity: 1; }
      50% { opacity: 0.45; }
    }
    .code-value {
      font-family: monospace;
      font-size: 12px;
      color: #475569;
    }

    /* Controls Bar */
    .controls-bar {
      margin-top: 20px;
      padding: 16px;
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      border-radius: 12px;
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 16px;
      flex-wrap: wrap;
    }
    .controls-status-info {
      font-size: 12.5px;
      font-weight: 600;
    }
    .connected-indicator { color: #15803d; }
    .hold-indicator { color: #b45309; }
    .ringing-indicator { color: #1d4ed8; }
    .terminal-indicator { color: #475569; }

    .actions-group {
      display: flex;
      align-items: center;
      gap: 10px;
      flex-wrap: wrap;
    }
    .btn {
      padding: 10px 16px;
      border-radius: 9px;
      font-size: 13px;
      font-weight: 750;
      cursor: pointer;
      display: inline-flex;
      align-items: center;
      gap: 6px;
      border: 1px solid transparent;
      transition: all 0.15s ease;
    }
    .btn:disabled {
      opacity: 0.55;
      cursor: not-allowed;
    }
    .btn-unmuted {
      background: #f8fafc;
      color: #334155;
      border-color: #cbd5e1;
    }
    .btn-unmuted:hover:not(:disabled) { background: #f1f5f9; }
    .btn-muted {
      background: #fef2f2;
      color: #dc2626;
      border-color: #fca5a5;
      font-weight: 700;
    }
    .btn-muted:hover:not(:disabled) { background: #fee2e2; }
    .btn-hold {
      background: #fffbeb;
      color: #b45309;
      border-color: #fde68a;
    }
    .btn-hold:hover:not(:disabled) { background: #fef3c7; }
    .btn-resume {
      background: #ecfdf5;
      color: #047857;
      border-color: #a7f3d0;
    }
    .btn-resume:hover:not(:disabled) { background: #d1fae5; }
    .btn-transfer {
      background: #eff6ff;
      color: #1d4ed8;
      border-color: #bfdbfe;
    }
    .btn-transfer:hover:not(:disabled) { background: #dbeafe; }
    .btn-end {
      background: #fff1f2;
      color: #be123c;
      border-color: #fecdd3;
    }
    .btn-end:hover:not(:disabled) { background: #ffe4e6; }
    .btn-accept {
      background: #15803d;
      color: #fff;
    }
    .btn-accept:hover:not(:disabled) { background: #166534; }
    .btn-reject {
      background: #fff1f2;
      color: #be123c;
      border-color: #fecdd3;
    }
    .btn-primary {
      background: #0284c7;
      color: #fff;
    }

    /* Notes Card */
    .notes-card {
      margin-bottom: 22px;
    }
    .save-status-tag {
      font-size: 11.5px;
      font-weight: 600;
      color: #15803d;
      background: #ecfdf5;
      padding: 3px 8px;
      border-radius: 6px;
      border: 1px solid #bbf7d0;
    }
    .notes-textarea {
      width: 100%;
      box-sizing: border-box;
      border: 1px solid #cbd5e1;
      border-radius: 10px;
      padding: 12px;
      font-family: inherit;
      font-size: 13.5px;
      color: #1e293b;
      resize: vertical;
      background: #f8fafc;
    }
    .notes-textarea:focus {
      outline: none;
      border-color: #0284c7;
      background: #fff;
      box-shadow: 0 0 0 3px rgba(2, 132, 199, 0.12);
    }
    .notes-footer {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-top: 10px;
    }
    .char-count {
      font-size: 11.5px;
      color: #94a3b8;
    }
    .btn-save-notes {
      background: #0284c7;
      color: #fff;
      border: none;
      padding: 8px 16px;
      border-radius: 8px;
      font-size: 12.5px;
      font-weight: 750;
      cursor: pointer;
    }
    .btn-save-notes:hover:not(:disabled) { background: #0369a1; }

    /* Customer Past History */
    .history-card {
      margin-bottom: 0;
    }
    .history-count {
      font-size: 11.5px;
      font-weight: 700;
      padding: 3px 8px;
      background: #f1f5f9;
      color: #475569;
      border-radius: 6px;
    }
    .history-loading, .history-empty {
      padding: 24px;
      text-align: center;
      font-size: 13px;
      color: #64748b;
      background: #f8fafc;
      border-radius: 10px;
      border: 1px dashed #cbd5e1;
    }
    .history-table-wrapper {
      overflow-x: auto;
      border: 1px solid #f1f5f9;
      border-radius: 10px;
    }
    .history-table {
      width: 100%;
      border-collapse: collapse;
      font-size: 12.5px;
      text-align: left;
    }
    .history-table th {
      background: #f8fafc;
      color: #64748b;
      font-weight: 700;
      padding: 10px 14px;
      border-bottom: 1px solid #e2e8f0;
      text-transform: uppercase;
      font-size: 10.5px;
      letter-spacing: 0.05em;
    }
    .history-table td {
      padding: 10px 14px;
      border-bottom: 1px solid #f1f5f9;
      color: #334155;
    }
    .mini-pill {
      padding: 2px 6px;
      border-radius: 4px;
      font-size: 10.5px;
      font-weight: 700;
      background: #eff6ff;
      color: #1d4ed8;
    }
    .mini-inbound { background: #f0fdf4; color: #15803d; }
    .mini-status {
      padding: 2px 6px;
      border-radius: 4px;
      font-size: 10.5px;
      font-weight: 700;
      background: #f1f5f9;
      color: #475569;
    }
    .status-completed { background: #ecfdf5; color: #047857; }
    .duration-cell { font-family: monospace; font-weight: 600; }

    /* Right Column: Timeline Feed */
    .timeline-card {
      position: sticky;
      top: 24px;
    }
    .btn-icon {
      background: none;
      border: 1px solid #e2e8f0;
      border-radius: 8px;
      padding: 6px 9px;
      cursor: pointer;
      font-size: 13px;
    }
    .btn-icon:hover { background: #f8fafc; }
    .timeline-loading, .timeline-empty {
      padding: 30px 16px;
      text-align: center;
      font-size: 13px;
      color: #64748b;
    }
    .timeline-feed {
      display: flex;
      flex-direction: column;
      gap: 0;
      margin-top: 10px;
    }
    .timeline-item {
      display: flex;
      gap: 14px;
      position: relative;
    }
    .timeline-icon-col {
      display: flex;
      flex-direction: column;
      align-items: center;
      width: 32px;
      flex-shrink: 0;
    }
    .timeline-icon {
      width: 28px;
      height: 28px;
      border-radius: 50%;
      display: grid;
      place-items: center;
      font-size: 13px;
      background: #f1f5f9;
      border: 1px solid #cbd5e1;
      z-index: 1;
    }
    .icon-connected { background: #ecfdf5; border-color: #a7f3d0; }
    .icon-hold { background: #fffbeb; border-color: #fde68a; }
    .icon-resumed { background: #ecfdf5; border-color: #a7f3d0; }
    .icon-transferred { background: #eff6ff; border-color: #bfdbfe; }
    .icon-notes { background: #f0fdfa; border-color: #99f6e4; }
    .icon-ended { background: #fef2f2; border-color: #fecaca; }

    .timeline-line {
      width: 2px;
      flex: 1;
      background: #e2e8f0;
      margin: 4px 0;
    }
    .timeline-content {
      flex: 1;
      padding-bottom: 18px;
    }
    .timeline-item-last .timeline-content {
      padding-bottom: 0;
    }
    .timeline-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 8px;
    }
    .timeline-event-name {
      font-size: 13px;
      color: #0f172a;
      font-weight: 750;
    }
    .timeline-time {
      font-size: 11px;
      color: #94a3b8;
    }
    .timeline-desc {
      margin: 3px 0 2px;
      font-size: 12px;
      color: #475569;
      line-height: 1.4;
    }
    .timeline-agent {
      display: inline-block;
      font-size: 10.5px;
      color: #64748b;
      font-weight: 600;
    }

    /* Transfer & Disposition Modals */
    .modal-backdrop {
      position: fixed;
      inset: 0;
      background: rgba(15, 23, 42, 0.55);
      backdrop-filter: blur(5px);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1000;
      padding: 16px;
      overflow-y: auto;
    }
    .modal-card {
      background: #fff;
      border-radius: 18px;
      width: 100%;
      max-width: 540px;
      max-height: min(880px, calc(100vh - 32px));
      display: flex;
      flex-direction: column;
      box-shadow: 0 25px 50px -12px rgba(15, 23, 42, 0.25);
      overflow: hidden;
      position: relative;
    }
    .modal-header {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      padding: 18px 24px;
      border-bottom: 1px solid #e2e8f0;
      background: #ffffff;
      flex-shrink: 0;
    }
    .modal-header h3 {
      font-size: 18px;
      font-weight: 800;
      color: #0f172a;
      margin: 0 0 2px;
    }
    .modal-header p {
      font-size: 12.5px;
      color: #64748b;
      margin: 0;
    }
    .modal-close {
      background: none;
      border: none;
      font-size: 24px;
      color: #94a3b8;
      cursor: pointer;
      line-height: 1;
      padding: 2px 6px;
      border-radius: 6px;
      transition: all 0.15s;
    }
    .modal-close:hover {
      background: #f1f5f9;
      color: #334155;
    }
    .modal-body {
      padding: 20px 24px;
      overflow-y: auto;
      flex: 1 1 auto;
      min-height: 0;
    }
    .transfer-tabs {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: 8px;
      margin-bottom: 16px;
    }
    .tab-btn {
      padding: 9px 6px;
      border-radius: 9px;
      font-size: 11.5px;
      font-weight: 750;
      cursor: pointer;
      border: 1px solid #cbd5e1;
      background: #f8fafc;
      color: #475569;
      transition: all 0.15s ease;
      text-align: center;
      white-space: nowrap;
    }
    .tab-btn:hover {
      background: #f1f5f9;
    }
    .active-tab {
      background: #0284c7;
      color: #fff;
      border-color: #0284c7;
    }
    .transfer-info-banner {
      display: flex;
      align-items: flex-start;
      gap: 12px;
      padding: 12px 14px;
      border-radius: 10px;
      margin-bottom: 16px;
      font-size: 12px;
      line-height: 1.45;
    }
    .banner-blind {
      background: #eff6ff;
      border: 1px solid #bfdbfe;
      color: #1e40af;
    }
    .banner-warm {
      background: #f0fdf4;
      border: 1px solid #bbf7d0;
      color: #166534;
    }
    .banner-supervisor {
      background: #fef2f2;
      border: 1px solid #fecaca;
      color: #991b1b;
    }
    .banner-queue {
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      color: #334155;
    }
    .info-icon {
      font-size: 18px;
      line-height: 1.2;
    }
    .info-text strong {
      display: block;
      margin-bottom: 2px;
    }
    .info-text p {
      margin: 0;
      opacity: 0.9;
    }
    .label-row {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 6px;
    }
    .label-row label {
      margin-bottom: 0;
    }
    .badge-count {
      font-size: 11px;
      font-weight: 700;
      padding: 2px 8px;
      border-radius: 9999px;
      background: #e0f2fe;
      color: #0369a1;
    }
    .target-preview-card {
      margin-top: 10px;
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 10px 12px;
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      border-radius: 10px;
    }
    .preview-avatar {
      width: 34px;
      height: 34px;
      border-radius: 50%;
      background: #e2e8f0;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 16px;
    }
    .preview-details {
      flex: 1;
    }
    .preview-title {
      display: flex;
      align-items: center;
      gap: 8px;
    }
    .preview-name {
      font-weight: 750;
      color: #0f172a;
      font-size: 13px;
    }
    .role-pill {
      font-size: 10.5px;
      font-weight: 700;
      padding: 1px 6px;
      border-radius: 6px;
      background: #e2e8f0;
      color: #334155;
    }
    .role-pill.supervisor-pill {
      background: #fee2e2;
      color: #b91c1c;
      border: 1px solid #fca5a5;
    }
    .preview-sub {
      font-size: 11px;
      color: #64748b;
      display: flex;
      gap: 6px;
      margin-top: 2px;
    }
    .status-online {
      color: #15803d;
      font-weight: 600;
    }
    .chips-row {
      display: flex;
      flex-wrap: wrap;
      gap: 6px;
      margin-bottom: 8px;
    }
    .chip-btn {
      background: #f1f5f9;
      border: 1px solid #cbd5e1;
      color: #334155;
      font-size: 11px;
      font-weight: 600;
      padding: 4px 10px;
      border-radius: 9999px;
      cursor: pointer;
      transition: all 0.15s ease;
    }
    .chip-btn:hover {
      background: #e2e8f0;
      border-color: #94a3b8;
    }
    .confirmation-box {
      margin-top: 14px;
      background: #fafaf9;
      border: 1px solid #e7e5e4;
      border-left: 4px solid #0284c7;
      border-radius: 8px;
      padding: 10px 14px;
      font-size: 12px;
    }
    .confirm-header {
      font-weight: 750;
      color: #0f172a;
      margin-bottom: 6px;
    }
    .confirm-body {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }
    .summary-line {
      display: flex;
      gap: 8px;
    }
    .summary-line .label {
      color: #64748b;
      min-width: 80px;
    }
    .summary-line .value.highlight {
      color: #0284c7;
    }
    .form-group {
      margin-bottom: 14px;
    }
    .form-group label {
      display: block;
      font-size: 12.5px;
      font-weight: 700;
      color: #334155;
      margin-bottom: 6px;
    }
    .required { color: #dc2626; }
    .form-select, .form-input {
      width: 100%;
      box-sizing: border-box;
      padding: 9px 12px;
      border: 1px solid #cbd5e1;
      border-radius: 8px;
      font-size: 13px;
      color: #1e293b;
      background: #fff;
    }
    .form-select:focus, .form-input:focus {
      outline: none;
      border-color: #0284c7;
      box-shadow: 0 0 0 3px rgba(2, 132, 199, 0.15);
    }
    .select-loading {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 10px;
      font-size: 12px;
      color: #64748b;
    }
    .select-empty-warning {
      padding: 10px 12px;
      border-radius: 8px;
      background: #fffbeb;
      border: 1px solid #fde68a;
      color: #92400e;
      font-size: 12px;
    }
    .modal-footer {
      display: flex;
      justify-content: flex-end;
      gap: 12px;
      padding: 16px 24px;
      border-top: 1px solid #e2e8f0;
      background: #f8fafc;
    }
    .secondary-btn {
      padding: 9px 16px;
      border-radius: 8px;
      font-size: 13px;
      font-weight: 700;
      border: 1px solid #cbd5e1;
      background: #fff;
      color: #334155;
      cursor: pointer;
    }
    .primary-btn {
      padding: 9px 16px;
      border-radius: 8px;
      font-size: 13px;
      font-weight: 700;
      border: none;
      background: #0284c7;
      color: #fff;
      cursor: pointer;
    }
    .transfer-confirm-btn:hover:not(:disabled) { background: #0369a1; }
    .transfer-confirm-btn:disabled { opacity: 0.55; cursor: not-allowed; }

    /* Disposition Modal Styles */
    .disposition-modal {
      max-width: 820px;
      width: 95%;
    }
    .disposition-grid {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 12px;
      margin-bottom: 16px;
      padding: 4px;
    }
    @media (max-width: 768px) {
      .disposition-grid {
        grid-template-columns: repeat(2, 1fr);
      }
    }
    @media (max-width: 500px) {
      .disposition-grid {
        grid-template-columns: 1fr;
      }
    }
    .disposition-card {
      padding: 12px 14px;
      border: 2px solid #e2e8f0;
      border-radius: 12px;
      cursor: pointer;
      background: #fff;
      transition: all 0.15s ease;
      display: flex;
      flex-direction: column;
      gap: 6px;
      text-align: left;
      min-height: 102px;
    }
    .disposition-card:hover {
      border-color: #38bdf8;
      background: #f8fafc;
      transform: translateY(-2px);
      box-shadow: 0 4px 12px rgba(15, 23, 42, 0.08);
    }
    .disposition-card.selected {
      border-color: #0284c7;
      background: #f0f9ff;
      box-shadow: 0 0 0 3px rgba(2, 132, 199, 0.2);
    }
    .disp-top {
      display: flex;
      align-items: center;
      gap: 8px;
    }
    .disp-icon {
      font-size: 18px;
    }
    .disp-name {
      font-weight: 700;
      font-size: 12.5px;
      color: #0f172a;
    }
    .disp-desc {
      font-size: 11px;
      color: #64748b;
      margin: 0;
      line-height: 1.35;
    }
    .disp-badges {
      display: flex;
      flex-wrap: wrap;
      gap: 4px;
      margin-top: auto;
      padding-top: 4px;
    }
    .disp-badge {
      font-size: 10px;
      font-weight: 700;
      padding: 2px 6px;
      border-radius: 4px;
    }
    .badge-warn {
      background: #fef3c7;
      color: #b45309;
      border: 1px solid #fde68a;
    }
    .badge-notes {
      background: #e0f2fe;
      color: #0369a1;
      border: 1px solid #bae6fd;
    }
    .followup-panel {
      background: #fffbeb;
      border: 1px solid #fcd34d;
      border-radius: 10px;
      padding: 12px 14px;
      margin-bottom: 14px;
    }
    .followup-panel .panel-tag {
      font-size: 11px;
      font-weight: 800;
      color: #92400e;
      margin-bottom: 8px;
      text-transform: uppercase;
      letter-spacing: 0.5px;
    }
    .wrapup-notes-panel .textarea {
      resize: vertical;
      font-family: inherit;
    }
    .complete-confirm-btn {
      background: #16a34a;
    }
    .complete-confirm-btn:hover:not(:disabled) {
      background: #15803d;
    }

    /* Empty / Loading States */
    .empty-state-card {
      margin-top: 40px;
      background: #fff;
      border: 1px solid #e2e8f0;
      border-radius: 20px;
      padding: 60px 20px;
      text-align: center;
      box-shadow: 0 4px 20px rgba(15, 23, 42, 0.04);
    }
    .empty-icon {
      font-size: 44px;
      margin-bottom: 12px;
    }
    .empty-state-card h3 {
      font-size: 20px;
      font-weight: 800;
      color: #0f172a;
      margin: 0 0 6px;
    }
    .empty-state-card p {
      color: #64748b;
      font-size: 14px;
      max-width: 480px;
      margin: 0 auto 20px;
    }
    .empty-actions {
      display: flex;
      justify-content: center;
      gap: 12px;
    }
    .loading-container {
      margin-top: 60px;
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 14px;
      color: #64748b;
      font-size: 14px;
    }
    .spinner {
      width: 32px;
      height: 32px;
      border: 3px solid #e2e8f0;
      border-top-color: #0284c7;
      border-radius: 50%;
      animation: spin 0.8s linear infinite;
    }
    .mini-spinner {
      width: 16px;
      height: 16px;
      border: 2px solid #cbd5e1;
      border-top-color: #0284c7;
      border-radius: 50%;
      animation: spin 0.8s linear infinite;
      display: inline-block;
    }
    @keyframes spin {
      to { transform: rotate(360deg); }
    }

    /* Responsive */
    @media (max-width: 960px) {
      .workspace-grid {
        grid-template-columns: 1fr;
      }
      .meta-grid {
        grid-template-columns: repeat(2, 1fr);
      }
      .timeline-card {
        position: static;
      }
    }
    @media (max-width: 640px) {
      .transfer-tabs {
        grid-template-columns: repeat(2, 1fr);
      }
      .page-header {
        flex-direction: column;
      }
      .meta-grid {
        grid-template-columns: 1fr;
      }
      .controls-bar {
        flex-direction: column;
        align-items: stretch;
      }
      .actions-group {
        flex-direction: column;
      }
      .actions-group .btn {
        width: 100%;
        justify-content: center;
      }
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ActiveCallComponent implements OnInit {
  private readonly sessionService = inject(IncomingCallSessionService);
  private readonly telephony = inject(TelephonyService);
  public readonly voiceService = inject(TwilioVoiceService);
  private readonly dispositionService = inject(DispositionService);
  private readonly callService = inject(CallService);
  private readonly customerService = inject(CustomerService);
  private readonly agentService = inject(AgentService);
  private readonly queueService = inject(QueueService);
  private readonly realtime = inject(CallCenterRealtimeService);
  private readonly agentDashboard = inject(AgentDashboardService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly destroyRef = inject(DestroyRef);

  CallStatus = CallStatus;

  session: ActiveCallSession | null = null;
  agent: AgentDashboard | null = null;
  customer: Customer | null = null;
  customerCalls: CustomerCallItem[] = [];
  customerCallsLoading = false;

  timeline: CallTimelineEvent[] = [];
  timelineLoading = false;

  notes = '';
  notesSavedAt: Date | null = null;

  currentStatus: CallStatus = CallStatus.Connected;
  durationSeconds = 0;
  callStartTime: number = Date.now();

  loadingSession = true;
  processing = false;
  activeAction: 'hold' | 'resume' | 'transfer' | 'end' | 'notes' | 'accept' | 'reject' | null = null;
  errorMessage = '';
  actionSuccessMessage = '';

  // Transfer Modal State
  TransferType = TransferType;
  transferModalOpen = false;
  transferMode: 'agent' | 'queue' = 'agent';
  transferStrategy: TransferType = TransferType.Blind;
  loadingTransferTargets = false;
  eligibleAgents: EligibleAgent[] = [];
  queues: CallQueue[] = [];
  selectedTargetAgentId = '';
  selectedTargetQueueId = '';
  transferReason = '';
  transferSubmitting = false;

  get selectedTargetAgent(): EligibleAgent | null {
    return this.eligibleAgents.find(a => a.agentId === this.selectedTargetAgentId) || null;
  }

  get selectedQueueName(): string {
    const q = this.queues.find(x => x.id === this.selectedTargetQueueId);
    return q ? `${q.name} (Priority: ${q.priority})` : '';
  }

  // Disposition & Wrap-Up Modal State
  dispositionModalOpen = false;
  loadingDispositions = false;
  dispositionLoadError = '';
  availableDispositions: CallDisposition[] = [];
  selectedDispositionId: string | null = null;
  selectedDisposition: CallDisposition | null = null;
  followUpDateTime = '';
  followUpNotes = '';
  wrapUpNotes = '';
  wrapUpErrorMessage = '';
  completingCall = false;
  isMuted = false;

  get minFollowUpDateTime(): string {
    const now = new Date(Date.now() + 5 * 60 * 1000); // 5 mins in future
    return now.toISOString().slice(0, 16);
  }

  get canSubmitDisposition(): boolean {
    if (!this.selectedDisposition) return false;
    if (this.selectedDisposition.requiresFollowUp) {
      if (!this.followUpDateTime) return false;
      const d = new Date(this.followUpDateTime);
      if (isNaN(d.getTime()) || d <= new Date()) return false;
    }
    if (this.selectedDisposition.requiresNotes) {
      if (!this.wrapUpNotes.trim()) return false;
    }
    return true;
  }

  get isTerminal(): boolean {
    return [
      CallStatus.Completed,
      CallStatus.Abandoned,
      CallStatus.Rejected,
      CallStatus.Failed
    ].includes(this.currentStatus);
  }

  get formattedDuration(): string {
    const hours = Math.floor(this.durationSeconds / 3600);
    const minutes = Math.floor((this.durationSeconds % 3600) / 60);
    const seconds = this.durationSeconds % 60;
    return `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`;
  }

  get callStartTimeFormatted(): string {
    return new Date(this.callStartTime).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });
  }

  get canSubmitTransfer(): boolean {
    if (this.transferMode === 'agent') {
      return Boolean(this.selectedTargetAgentId);
    }
    return Boolean(this.selectedTargetQueueId);
  }

  ngOnInit(): void {
    this.loadAgentProfile();
    this.hydrateSession();

    // Softphone WebRTC Device Initialization
    void this.voiceService.initDevice();
    this.voiceService.isMuted$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(muted => {
        this.isMuted = muted;
        this.cdr.markForCheck();
      });

    this.voiceService.callState$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(callState => {
        if (callState === 'ended' && !this.isTerminal) {
          this.currentStatus = CallStatus.Completed;
          if (!this.dispositionModalOpen && !this.selectedDisposition) {
            this.openDispositionModal();
          }
          this.cdr.markForCheck();
        }
      });

    // Duration timer ticker (1-second tick)
    interval(1000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        if (!this.isTerminal && this.currentStatus !== CallStatus.OnHold) {
          this.updateDuration();
          this.cdr.markForCheck();
        }
      });

    // Realtime SignalR event handling
    this.realtime.connect();
    this.realtime.events$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(event => {
        if (event.type === 'call-status') {
          const payload = event.payload as {
            callId?: string;
            status?: string | number;
            occurredAtUtc?: string;
          };

          if (payload.callId === this.session?.event.callId && payload.status !== undefined) {
            const newStatus = this.parseStatus(payload.status);
            this.currentStatus = newStatus;

            if (newStatus === CallStatus.Connected && payload.occurredAtUtc) {
              this.callStartTime = Date.parse(payload.occurredAtUtc);
              this.updateDuration();
            }

            if (this.isTerminal && !this.dispositionModalOpen && !this.selectedDisposition) {
              this.openDispositionModal();
            }

            this.loadTimeline();
            this.cdr.markForCheck();
          }
        }

        if (event.type === 'call-transferred') {
          const payload = event.payload as CallTransferredEvent;
          if (payload.callId === this.session?.event.callId) {
            if (payload.fromAgentId === this.agent?.agentId && payload.toAgentId !== this.agent?.agentId) {
              this.actionSuccessMessage = `Call transferred to ${payload.transferType === 'Queue' ? 'queue' : 'another agent'}.`;
              this.sessionService.clear();
              this.cdr.markForCheck();
              setTimeout(() => {
                void this.router.navigate(['/agent-dashboard']);
              }, 1500);
            }
            this.loadTimeline();
            this.cdr.markForCheck();
          }
        }
      });
  }

  private hydrateSession(): void {
    this.loadingSession = true;
    const existing = this.sessionService.snapshot;

    if (existing) {
      this.session = existing;
      this.initSessionData(existing.event.callId, existing.customer?.id || existing.event.customerId);
      this.loadingSession = false;
      this.cdr.markForCheck();
      return;
    }

    // Auto-hydrate from backend agent dashboard if page was reloaded
    this.agentDashboard.getMyDashboard().subscribe({
      next: dashboard => {
        this.agent = dashboard;
        if (dashboard.currentCall) {
          const current = dashboard.currentCall;
          this.session = {
            event: {
              callId: current.id,
              customerId: current.customerId,
              phoneNumber: current.phoneNumber,
              direction: current.direction === CallDirection.Outbound ? 'Outbound' : 'Inbound',
              status: CallStatus[current.status] ?? 'Connected',
              occurredAtUtc: current.startedAt
            },
            customer: {
              id: current.customerId,
              fullName: current.customerName || 'Customer',
              phone: current.customerPhone || current.phoneNumber
            }
          };
          this.sessionService.setSession(this.session);
          this.initSessionData(current.id, current.customerId);
        } else {
          // Check query parameters
          const queryCallId = this.route.snapshot.queryParamMap.get('callId');
          if (queryCallId) {
            this.fetchCallById(queryCallId);
          }
        }
        this.loadingSession = false;
        this.cdr.markForCheck();
      },
      error: () => {
        this.loadingSession = false;
        this.cdr.markForCheck();
      }
    });
  }

  private fetchCallById(callId: string): void {
    this.callService.getById(callId).subscribe({
      next: call => {
        this.session = {
          event: {
            callId: call.id,
            customerId: call.customerId,
            phoneNumber: call.phoneNumber,
            direction: typeof call.direction === 'number' ? (call.direction === 2 ? 'Outbound' : 'Inbound') : String(call.direction),
            status: typeof call.status === 'number' ? CallStatus[call.status] : String(call.status),
            occurredAtUtc: call.startedAt
          },
          customer: {
            id: call.customerId,
            fullName: 'Customer',
            phone: call.phoneNumber
          }
        };
        this.sessionService.setSession(this.session);
        this.initSessionData(call.id, call.customerId);
        this.cdr.markForCheck();
      },
      error: () => {
        this.cdr.markForCheck();
      }
    });
  }

  private initSessionData(callId: string, customerId?: string | null): void {
    if (this.session) {
      this.currentStatus = this.parseStatus(this.session.event.status);
      this.callStartTime = this.session.event.occurredAtUtc
        ? Date.parse(this.session.event.occurredAtUtc)
        : Date.now();
      this.updateDuration();
    }

    // Load Timeline
    this.loadTimeline();

    // Fetch call details for existing notes
    this.callService.getById(callId).subscribe({
      next: (call: CallResponse) => {
        if (call.notes) {
          this.notes = call.notes;
        }
        this.cdr.markForCheck();
      }
    });

    // Fetch full Customer Details & History
    if (customerId) {
      this.customerService.getDetails(customerId).subscribe({
        next: (cust: Customer) => {
          this.customer = cust;
          this.cdr.markForCheck();
        }
      });

      this.customerCallsLoading = true;
      this.customerService.getCustomerCalls(customerId, 1, 5)
        .pipe(finalize(() => {
          this.customerCallsLoading = false;
          this.cdr.markForCheck();
        }))
        .subscribe({
          next: result => {
            this.customerCalls = result.items || [];
            this.cdr.markForCheck();
          },
          error: () => {
            this.customerCalls = [];
          }
        });
    }
  }

  private loadAgentProfile(): void {
    this.agentDashboard.getMyDashboard().subscribe({
      next: dashboard => {
        this.agent = dashboard;
        this.cdr.markForCheck();
      },
      error: () => {
        this.cdr.markForCheck();
      }
    });
  }

  loadTimeline(): void {
    if (!this.session) return;
    this.timelineLoading = true;
    this.callService.getTimeline(this.session.event.callId)
      .pipe(finalize(() => {
        this.timelineLoading = false;
        this.cdr.markForCheck();
      }))
      .subscribe({
        next: timeline => {
          this.timeline = timeline || [];
          this.cdr.markForCheck();
        },
        error: () => {
          this.timeline = [];
        }
      });
  }

  private updateDuration(): void {
    this.durationSeconds = Math.max(0, Math.floor((Date.now() - this.callStartTime) / 1000));
  }

  /* Actions */
  acceptCall(): void {
    if (!this.session || this.processing) return;
    this.processing = true;
    this.activeAction = 'accept';
    this.errorMessage = '';

    this.telephony.acceptCall(this.session.event.callId)
      .pipe(finalize(() => {
        this.processing = false;
        this.activeAction = null;
        this.cdr.markForCheck();
      }))
      .subscribe({
        next: () => {
          this.currentStatus = CallStatus.Connected;
          this.actionSuccessMessage = 'Call connected successfully.';
          void this.voiceService.acceptCall();
          this.loadTimeline();
          this.cdr.markForCheck();
        },
        error: err => {
          this.errorMessage = err?.error?.message || 'Unable to accept call.';
          this.cdr.markForCheck();
        }
      });
  }

  rejectCall(): void {
    if (!this.session || this.processing) return;
    this.processing = true;
    this.activeAction = 'reject';
    this.errorMessage = '';

    this.voiceService.rejectCall();
    this.telephony.rejectCall(this.session.event.callId)
      .pipe(finalize(() => {
        this.processing = false;
        this.activeAction = null;
        this.cdr.markForCheck();
      }))
      .subscribe({
        next: () => {
          this.sessionService.clear();
          void this.router.navigate(['/incoming-call']);
        },
        error: err => {
          this.errorMessage = err?.error?.message || 'Unable to reject call.';
          this.cdr.markForCheck();
        }
      });
  }

  holdCall(): void {
    if (!this.session || this.processing || this.isTerminal) return;
    this.processing = true;
    this.activeAction = 'hold';
    this.errorMessage = '';
    this.actionSuccessMessage = '';

    this.telephony.holdCall(this.session.event.callId)
      .pipe(finalize(() => {
        this.processing = false;
        this.activeAction = null;
        this.cdr.markForCheck();
      }))
      .subscribe({
        next: () => {
          this.currentStatus = CallStatus.OnHold;
          this.actionSuccessMessage = 'Call placed on hold.';
          this.loadTimeline();
          this.cdr.markForCheck();
        },
        error: err => {
          this.errorMessage = err?.error?.message || 'Unable to place call on hold.';
          this.cdr.markForCheck();
        }
      });
  }

  resumeCall(): void {
    if (!this.session || this.processing || this.isTerminal) return;
    this.processing = true;
    this.activeAction = 'resume';
    this.errorMessage = '';
    this.actionSuccessMessage = '';

    this.telephony.resumeCall(this.session.event.callId)
      .pipe(finalize(() => {
        this.processing = false;
        this.activeAction = null;
        this.cdr.markForCheck();
      }))
      .subscribe({
        next: () => {
          this.currentStatus = CallStatus.Connected;
          this.actionSuccessMessage = 'Call resumed.';
          this.loadTimeline();
          this.cdr.markForCheck();
        },
        error: err => {
          this.errorMessage = err?.error?.message || 'Unable to resume call.';
          this.cdr.markForCheck();
        }
      });
  }

  toggleMute(): void {
    if (this.processing || this.isTerminal || this.currentStatus === CallStatus.OnHold) return;
    this.isMuted = this.voiceService.toggleMute();
    this.cdr.markForCheck();
  }

  openTransferModal(): void {
    if (this.isTerminal || !this.session) return;
    this.transferModalOpen = true;
    this.transferMode = 'agent';
    this.transferStrategy = TransferType.Blind;
    this.selectedTargetAgentId = '';
    this.selectedTargetQueueId = '';
    this.transferReason = '';
    this.loadTransferTargets();
  }

  closeTransferModal(): void {
    if (this.transferSubmitting) return;
    this.transferModalOpen = false;
    this.cdr.markForCheck();
  }

  setTransferStrategy(strategy: TransferType): void {
    if (this.transferMode === 'agent' && this.transferStrategy === strategy) return;
    this.transferMode = 'agent';
    this.transferStrategy = strategy;
    this.selectedTargetAgentId = '';
    this.loadEligibleAgents();
  }

  setTransferModeQueue(): void {
    if (this.transferMode === 'queue') return;
    this.transferMode = 'queue';
    this.selectedTargetQueueId = '';
    this.loadQueues();
  }

  loadTransferTargets(): void {
    this.loadEligibleAgents();
    this.loadQueues();
  }

  loadEligibleAgents(): void {
    if (!this.session) return;
    this.loadingTransferTargets = true;
    this.cdr.markForCheck();

    this.telephony.getEligibleTransferAgents(this.session.event.callId, this.transferStrategy)
      .pipe(
        finalize(() => {
          this.loadingTransferTargets = false;
          this.cdr.markForCheck();
        })
      )
      .subscribe({
        next: agents => {
          this.eligibleAgents = (agents || []).filter(a => a.isEligible);
          this.cdr.markForCheck();
        },
        error: () => {
          this.errorMessage = 'Failed to load eligible transfer agents.';
          this.cdr.markForCheck();
        }
      });
  }

  loadQueues(): void {
    this.queueService.getQueues()
      .subscribe({
        next: queues => {
          this.queues = (queues || []).filter(q => q.isActive);
          this.cdr.markForCheck();
        },
        error: () => {
          // silently handle
        }
      });
  }

  onAgentSelected(agentId: string): void {
    this.selectedTargetAgentId = agentId;
    this.cdr.markForCheck();
  }

  applyReasonChip(reason: string): void {
    this.transferReason = reason;
    this.cdr.markForCheck();
  }

  getTransferStrategyLabel(type: TransferType): string {
    switch (type) {
      case TransferType.Blind: return 'Blind (Cold) Transfer';
      case TransferType.Warm: return 'Warm (Consultative) Transfer';
      case TransferType.Supervisor: return 'Supervisor Escalation Transfer';
      default: return 'Transfer';
    }
  }

  getStrategyBannerClass(): string {
    if (this.transferMode === 'queue') return 'banner-queue';
    switch (this.transferStrategy) {
      case TransferType.Blind: return 'banner-blind';
      case TransferType.Warm: return 'banner-warm';
      case TransferType.Supervisor: return 'banner-supervisor';
      default: return '';
    }
  }

  getStrategyBannerIcon(): string {
    if (this.transferMode === 'queue') return '📋';
    switch (this.transferStrategy) {
      case TransferType.Blind: return '⚡';
      case TransferType.Warm: return '🤝';
      case TransferType.Supervisor: return '🛡️';
      default: return '🔀';
    }
  }

  getStrategyBannerTitle(): string {
    if (this.transferMode === 'queue') return 'Call Queue Reassignment';
    switch (this.transferStrategy) {
      case TransferType.Blind: return 'Blind Transfer (Cold Handover)';
      case TransferType.Warm: return 'Warm Transfer (Consultative Handover)';
      case TransferType.Supervisor: return 'Supervisor Priority Escalation';
      default: return 'Transfer Call';
    }
  }

  getStrategyBannerDesc(): string {
    if (this.transferMode === 'queue') {
      return 'Place customer back into an active queue. The next available agent serving this queue will receive the call automatically.';
    }
    switch (this.transferStrategy) {
      case TransferType.Blind:
        return 'Direct immediate transfer without prior consultation. The call is instantly assigned to the chosen agent.';
      case TransferType.Warm:
        return 'Consultative transfer. The customer is placed on hold while the handover is initiated and confirmed with the target agent.';
      case TransferType.Supervisor:
        return 'Priority transfer strictly filtered to active on-duty Supervisors and Administrators for critical handling.';
      default:
        return '';
    }
  }

  submitTransfer(): void {
    if (!this.session || this.transferSubmitting || !this.canSubmitTransfer) return;

    this.transferSubmitting = true;
    this.errorMessage = '';

    const req: TransferCallRequest = {
      targetAgentId: this.transferMode === 'agent' ? this.selectedTargetAgentId : null,
      targetQueueId: this.transferMode === 'queue' ? this.selectedTargetQueueId : null,
      transferType: this.transferMode === 'agent' ? this.transferStrategy : undefined,
      reason: this.transferReason.trim() || (this.transferMode === 'queue' ? 'Queue Reassignment' : this.getTransferStrategyLabel(this.transferStrategy))
    };

    this.telephony.transferCall(this.session.event.callId, req)
      .pipe(finalize(() => {
        this.transferSubmitting = false;
        this.cdr.markForCheck();
      }))
      .subscribe({
        next: () => {
          this.transferModalOpen = false;
          this.actionSuccessMessage = 'Call successfully transferred!';
          this.sessionService.clear();
          this.cdr.markForCheck();
          setTimeout(() => {
            void this.router.navigate(['/agent-dashboard']);
          }, 1200);
        },
        error: err => {
          this.errorMessage = err?.error?.message || 'Transfer failed. Target agent or queue might be unavailable.';
          this.cdr.markForCheck();
        }
      });
  }

  endCall(): void {
    if (!this.session || this.processing || this.isTerminal) return;
    this.openDispositionModal();
  }

  openDispositionModal(): void {
    if (!this.session || this.processing || this.isTerminal) return;
    this.dispositionModalOpen = true;
    this.wrapUpErrorMessage = '';
    this.wrapUpNotes = this.notes || '';
    if (!this.followUpDateTime) {
      // Default follow-up date to tomorrow same time
      const tomorrow = new Date(Date.now() + 24 * 60 * 60 * 1000);
      this.followUpDateTime = tomorrow.toISOString().slice(0, 16);
    }
    if (this.availableDispositions.length === 0) {
      this.loadDispositions();
    }
    this.cdr.markForCheck();
  }

  closeDispositionModal(): void {
    if (this.completingCall || this.isTerminal) return;
    this.dispositionModalOpen = false;
    this.wrapUpErrorMessage = '';
    this.cdr.markForCheck();
  }

  loadDispositions(): void {
    this.loadingDispositions = true;
    this.dispositionLoadError = '';
    this.cdr.markForCheck();

    this.dispositionService.getDispositions(false)
      .pipe(
        finalize(() => {
          this.loadingDispositions = false;
          this.cdr.markForCheck();
        })
      )
      .subscribe({
        next: list => {
          this.availableDispositions = (list || []).sort((a, b) => a.sortOrder - b.sortOrder);
          if (!this.selectedDispositionId && this.availableDispositions.length > 0) {
            this.selectDisposition(this.availableDispositions[0]);
          }
          this.cdr.markForCheck();
        },
        error: () => {
          this.dispositionLoadError = 'Failed to load dispositions. Please try again.';
          this.cdr.markForCheck();
        }
      });
  }

  selectDisposition(disp: CallDisposition): void {
    this.selectedDispositionId = disp.id;
    this.selectedDisposition = disp;
    this.wrapUpErrorMessage = '';
    this.cdr.markForCheck();
  }

  getDispositionIcon(code: string): string {
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

  submitCallCompletion(): void {
    if (!this.session || !this.selectedDisposition || !this.canSubmitDisposition || this.completingCall) {
      return;
    }

    const req: CompleteCallRequest = {
      dispositionId: this.selectedDisposition.id,
      notes: this.wrapUpNotes.trim() || undefined
    };

    if (this.selectedDisposition.requiresFollowUp) {
      req.followUpAt = new Date(this.followUpDateTime).toISOString();
      req.followUpNotes = this.followUpNotes.trim() || undefined;
    }

    this.completingCall = true;
    this.wrapUpErrorMessage = '';
    this.cdr.markForCheck();

    this.telephony.completeCall(this.session.event.callId, req)
      .pipe(
        finalize(() => {
          this.completingCall = false;
          this.cdr.markForCheck();
        })
      )
      .subscribe({
        next: () => {
          this.currentStatus = CallStatus.Completed;
          this.voiceService.disconnectCall();
          this.sessionService.clear();
          this.actionSuccessMessage = `Call wrapped up successfully with outcome: ${this.selectedDisposition?.name}!`;
          this.dispositionModalOpen = false;
          this.loadTimeline();
          this.cdr.markForCheck();
          setTimeout(() => {
            void this.router.navigate(['/agent-dashboard']);
          }, 1200);
        },
        error: err => {
          this.wrapUpErrorMessage = err?.error?.message || 'Failed to complete call wrap-up.';
          this.cdr.markForCheck();
        }
      });
  }

  saveNotes(): void {
    if (!this.session || !this.notes.trim()) return;
    this.processing = true;
    this.activeAction = 'notes';
    this.errorMessage = '';

    this.callService.updateNotes(this.session.event.callId, this.notes)
      .pipe(finalize(() => {
        this.processing = false;
        this.activeAction = null;
        this.cdr.markForCheck();
      }))
      .subscribe({
        next: () => {
          this.notesSavedAt = new Date();
          this.loadTimeline();
          this.cdr.markForCheck();
        },
        error: err => {
          this.errorMessage = err?.error?.message || 'Failed to save notes.';
          this.cdr.markForCheck();
        }
      });
  }

  navigateToDashboard(): void {
    this.sessionService.clear();
    void this.router.navigate(['/agent-dashboard']);
  }

  /* Helpers */
  initials(name: string): string {
    return name
      .split(' ')
      .filter(Boolean)
      .slice(0, 2)
      .map(part => part[0])
      .join('')
      .toUpperCase();
  }

  statusLabel(status: CallStatus): string {
    switch (status) {
      case CallStatus.Queued: return 'Queued';
      case CallStatus.Ringing: return 'Ringing';
      case CallStatus.Connected: return 'Connected';
      case CallStatus.Completed: return 'Completed';
      case CallStatus.Abandoned: return 'Abandoned';
      case CallStatus.Rejected: return 'Rejected';
      case CallStatus.Failed: return 'Failed';
      case CallStatus.OnHold: return 'On Hold';
      default: return 'Unknown';
    }
  }

  statusBadgeClass(status: CallStatus): string {
    switch (status) {
      case CallStatus.Connected: return 'badge-connected';
      case CallStatus.OnHold: return 'badge-hold';
      case CallStatus.Ringing: return 'badge-ringing';
      default: return 'badge-terminal';
    }
  }

  timelineIconEmoji(type: string): string {
    const t = type.toLowerCase();
    if (t.includes('initiated') || t.includes('created')) return '📞';
    if (t.includes('queue')) return '⏳';
    if (t.includes('ringing')) return '🔔';
    if (t.includes('resumed')) return '▶️';
    if (t.includes('connected') || t.includes('accept')) return '🟢';
    if (t.includes('hold')) return '⏸️';
    if (t.includes('transfer')) return '🔀';
    if (t.includes('notes')) return '📝';
    if (t.includes('ended') || t.includes('complete')) return '🛑';
    return '📌';
  }

  timelineIconClass(type: string): string {
    const t = type.toLowerCase();
    if (t.includes('hold')) return 'icon-hold';
    if (t.includes('resumed')) return 'icon-resumed';
    if (t.includes('connected') || t.includes('accept')) return 'icon-connected';
    if (t.includes('transfer')) return 'icon-transferred';
    if (t.includes('notes')) return 'icon-notes';
    if (t.includes('ended') || t.includes('complete')) return 'icon-ended';
    return '';
  }

  isDirectionInbound(direction: string | number): boolean {
    if (typeof direction === 'number') return direction === 1;
    return String(direction).toLowerCase().includes('inbound');
  }

  formatDirection(direction: string | number): string {
    return this.isDirectionInbound(direction) ? 'Inbound' : 'Outbound';
  }

  isCallCompleted(status: string | number): boolean {
    if (typeof status === 'number') return status === CallStatus.Completed;
    return String(status).toLowerCase().includes('complete');
  }

  formatCallStatus(status: string | number): string {
    if (typeof status === 'number') return this.statusLabel(status as CallStatus);
    return String(status);
  }

  formatSeconds(seconds?: number | null): string {
    if (!seconds || seconds <= 0) return '00:00';
    const m = Math.floor(seconds / 60);
    const s = seconds % 60;
    return `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
  }

  private parseStatus(status: string | number): CallStatus {
    if (typeof status === 'number') return status as CallStatus;
    const normalized = String(status).trim().toLowerCase();
    const numeric = Number(normalized);
    if (Number.isInteger(numeric) && numeric >= CallStatus.Queued && numeric <= CallStatus.OnHold) {
      return numeric as CallStatus;
    }
    const map: Record<string, CallStatus> = {
      queued: CallStatus.Queued,
      ringing: CallStatus.Ringing,
      connected: CallStatus.Connected,
      completed: CallStatus.Completed,
      abandoned: CallStatus.Abandoned,
      rejected: CallStatus.Rejected,
      failed: CallStatus.Failed,
      onhold: CallStatus.OnHold,
      hold: CallStatus.OnHold,
      'on-hold': CallStatus.OnHold,
      'on_hold': CallStatus.OnHold
    };
    return map[normalized] ?? CallStatus.Connected;
  }
}
