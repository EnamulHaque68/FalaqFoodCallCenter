import { ChangeDetectionStrategy, ChangeDetectorRef, Component, DestroyRef, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, finalize, of } from 'rxjs';
import { FormsModule } from '@angular/forms';
import { CallCenterRealtimeService } from '../../core/services/call-center-realtime.service';
import { CustomerService } from '../../core/services/customer.service';
import { TelephonyService } from '../../core/services/telephony.service';
import { IncomingCallSessionService } from '../../core/services/incoming-call-session.service';
import { CustomerSummary, IncomingCallEvent } from '../../core/models/incoming-call.models';

@Component({
  selector: 'app-incoming-call',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="incoming-page">
      <div class="page-header">
        <div>
          <span class="eyebrow">Real-Time Workspace</span>
          <h2>Incoming Call Center</h2>
          <p>Live incoming customer calls arrive automatically via SignalR.</p>
        </div>
        <div class="header-actions">
          <button type="button" class="sim-toggle-btn" (click)="toggleSimPanel()">
            <span>⚡</span> Simulate Test Call
          </button>
          <span class="connection-badge">
            <span class="status-dot online"></span> SignalR Live
          </span>
        </div>
      </div>

      <!-- Quick Simulator Drawer / Panel -->
      @if (showSimPanel) {
        <section class="sim-panel">
          <div class="sim-header">
            <strong>Telephony Simulator</strong>
            <button type="button" class="close-sim-btn" (click)="showSimPanel = false">✕</button>
          </div>
          <p class="sim-desc">Dispatch a simulated customer call to test routing, ringing, and queueing.</p>
          <div class="sim-form-row">
            <label>
              Phone Number
              <input
                type="text"
                [(ngModel)]="simPhoneNumber"
                placeholder="e.g. 8801712345678" />
            </label>
            <button
              type="button"
              class="sim-submit-btn"
              [disabled]="simulating"
              (click)="triggerSimulatedCall()">
              {{ simulating ? 'Simulating...' : 'Ring Call Center' }}
            </button>
          </div>
        </section>
      }

      @if (loading) {
        <div class="state-card">
          <div class="spinner"></div>
          <strong>Retrieving caller information…</strong>
          <span>Synchronizing customer records with CRM.</span>
        </div>
      } @else if (errorMessage && !call) {
        <div class="state-card error-state">
          <strong>Telephony Notification</strong>
          <span>{{ errorMessage }}</span>
        </div>
      } @else if (call) {
        <section class="incoming-card" [class.ringing-pulse]="isRinging">
          <div class="incoming-accent"></div>
          <div class="incoming-top">
            <div class="caller-intro">
              <div class="ring-bell-icon">
                <span class="bell-wave"></span>
                <span>🔔</span>
              </div>
              <div>
                <span class="eyebrow">Inbound Customer Call</span>
                <h3>{{ customer?.fullName || 'Caller: ' + call.phoneNumber }}</h3>
              </div>
            </div>
            <span class="call-status-pill" [ngClass]="statusClass(call.status)">
              <span class="pulse-dot"></span>
              {{ call.status }}
            </span>
          </div>

          <div class="caller-grid">
            <div class="grid-cell">
              <span>Customer Phone</span>
              <strong>{{ customer?.phone || call.phoneNumber }}</strong>
            </div>
            <div class="grid-cell">
              <span>Customer Name</span>
              <strong>{{ customer?.fullName || 'First-time Caller (Unknown)' }}</strong>
            </div>
            <div class="grid-cell">
              <span>Routing Status</span>
              <strong>{{ call.status }}</strong>
            </div>
            <div class="grid-cell">
              <span>Call Direction</span>
              <strong>Inbound Voice</strong>
            </div>
          </div>

          @if (errorMessage) {
            <div class="inline-error">{{ errorMessage }}</div>
          }

          <div class="call-actions">
            <button
              class="reject-button"
              type="button"
              [disabled]="processing"
              (click)="reject()">
              <span>🚫</span> {{ processing && action === 'reject' ? 'Rejecting…' : 'Reject Call' }}
            </button>
            <button
              class="accept-button"
              type="button"
              [disabled]="processing"
              (click)="accept()">
              <span>📞</span> {{ processing && action === 'accept' ? 'Connecting…' : 'Accept Call' }}
            </button>
          </div>
        </section>
      } @else {
        <div class="state-card idle-state">
          <div class="phone-ring">☎</div>
          <strong>Awaiting Incoming Calls</strong>
          <span>Ready to receive callers. When a customer dials in, this console will ring automatically.</span>
        </div>
      }
    </div>
  `,
  styles: [`
    .incoming-page {
      max-width: 1050px;
    }
    .page-header {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      flex-wrap: wrap;
      gap: 16px;
    }
    .header-actions {
      display: flex;
      align-items: center;
      gap: 12px;
    }
    .sim-toggle-btn {
      background: #f1f5f9;
      border: 1px solid #cbd5e1;
      padding: 7px 14px;
      border-radius: 999px;
      font-size: 12px;
      font-weight: 600;
      color: #334155;
      cursor: pointer;
      display: inline-flex;
      align-items: center;
      gap: 6px;
      transition: all 0.15s ease;
    }
    .sim-toggle-btn:hover {
      background: #e2e8f0;
      color: #0f172a;
    }
    .connection-badge {
      display: flex;
      align-items: center;
      gap: 7px;
      padding: 7px 12px;
      border: 1px solid #dce4ee;
      border-radius: 999px;
      background: #fff;
      color: #475569;
      font-size: 11px;
      font-weight: 700;
    }
    .status-dot.online {
      width: 7px;
      height: 7px;
      border-radius: 50%;
      background: #16a34a;
      box-shadow: 0 0 0 3px #dcfce7;
    }

    /* Simulation Drawer */
    .sim-panel {
      margin-top: 16px;
      background: #f8fafc;
      border: 1px dashed #cbd5e1;
      border-radius: 14px;
      padding: 16px 20px;
    }
    .sim-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      font-size: 13.5px;
      color: #0f172a;
    }
    .close-sim-btn {
      background: transparent;
      border: none;
      color: #94a3b8;
      cursor: pointer;
      font-size: 14px;
    }
    .sim-desc {
      font-size: 12px;
      color: #64748b;
      margin: 4px 0 12px;
    }
    .sim-form-row {
      display: flex;
      align-items: flex-end;
      gap: 12px;
      flex-wrap: wrap;
    }
    .sim-form-row label {
      display: flex;
      flex-direction: column;
      gap: 4px;
      font-size: 11px;
      font-weight: 600;
      text-transform: uppercase;
      color: #475569;
    }
    .sim-form-row input {
      padding: 8px 12px;
      border: 1px solid #cbd5e1;
      border-radius: 8px;
      font-size: 13px;
      min-width: 220px;
    }
    .sim-submit-btn {
      background: #0f172a;
      color: #ffffff;
      border: 1px solid #0f172a;
      padding: 8px 16px;
      border-radius: 8px;
      font-weight: 600;
      font-size: 13px;
      cursor: pointer;
    }
    .sim-submit-btn:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }

    /* Active Ringing Card */
    .incoming-card {
      position: relative;
      overflow: hidden;
      margin-top: 24px;
      background: #fff;
      border: 2px solid #16a34a;
      border-radius: 20px;
      padding: 30px;
      box-shadow: 0 12px 35px rgba(22, 163, 74, 0.12);
      transition: all 0.2s ease;
    }
    .incoming-card.ringing-pulse {
      animation: pulse-border 2s infinite ease-in-out;
    }
    .incoming-accent {
      position: absolute;
      left: 0;
      top: 0;
      bottom: 0;
      width: 6px;
      background: #16a34a;
    }
    .incoming-top {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: 20px;
    }
    .caller-intro {
      display: flex;
      align-items: center;
      gap: 14px;
    }
    .ring-bell-icon {
      width: 48px;
      height: 48px;
      border-radius: 14px;
      background: #dcfce7;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 22px;
      position: relative;
      animation: bell-tilt 0.6s infinite ease-in-out alternate;
    }
    .incoming-top h3 {
      margin: 4px 0 0;
      font-size: 24px;
      font-weight: 800;
      color: #0f172a;
    }
    .call-status-pill {
      padding: 6px 12px;
      border-radius: 9999px;
      font-size: 12px;
      font-weight: 700;
      display: flex;
      align-items: center;
      gap: 6px;
    }
    .pulse-dot {
      width: 7px;
      height: 7px;
      border-radius: 50%;
      background: currentColor;
    }
    .call-status-pill.ringing {
      background: #ecfdf5;
      color: #15803d;
    }
    .call-status-pill.queued {
      background: #fef3c7;
      color: #b45309;
    }

    .caller-grid {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 18px;
      margin-top: 26px;
      padding: 20px 0;
      border-top: 1px solid #edf1f5;
      border-bottom: 1px solid #edf1f5;
    }
    .grid-cell span {
      display: block;
      font-size: 11px;
      color: #94a3b8;
      text-transform: uppercase;
      letter-spacing: 0.06em;
      font-weight: 600;
    }
    .grid-cell strong {
      display: block;
      margin-top: 4px;
      font-size: 15px;
      color: #1e293b;
    }

    .inline-error {
      margin-top: 14px;
      padding: 8px 12px;
      border-radius: 8px;
      background: #fef2f2;
      color: #b91c1c;
      font-size: 12.5px;
    }

    .call-actions {
      display: flex;
      justify-content: flex-end;
      gap: 12px;
      margin-top: 24px;
    }
    .call-actions button {
      min-width: 140px;
      padding: 12px 20px;
      border-radius: 12px;
      font-weight: 700;
      font-size: 13.5px;
      cursor: pointer;
      display: inline-flex;
      align-items: center;
      justify-content: center;
      gap: 8px;
      transition: all 0.15s ease;
    }
    .call-actions button:disabled {
      opacity: 0.55;
      cursor: not-allowed;
    }
    .reject-button {
      border: 1px solid #fecdd3;
      background: #fff1f2;
      color: #be123c;
    }
    .reject-button:hover:not(:disabled) {
      background: #ffe4e6;
    }
    .accept-button {
      border: 1px solid #15803d;
      background: #15803d;
      color: #fff;
      box-shadow: 0 4px 12px rgba(21, 128, 61, 0.25);
    }
    .accept-button:hover:not(:disabled) {
      background: #166534;
    }

    /* State Cards */
    .state-card {
      margin-top: 24px;
      min-height: 330px;
      background: #fff;
      border: 1px solid #dfe5ec;
      border-radius: 18px;
      display: flex;
      align-items: center;
      justify-content: center;
      flex-direction: column;
      gap: 8px;
      text-align: center;
      color: #718096;
      padding: 40px 20px;
    }
    .state-card strong {
      color: #334155;
      font-size: 16px;
    }
    .state-card span {
      font-size: 12.5px;
      max-width: 420px;
    }
    .spinner {
      width: 24px;
      height: 24px;
      border: 2px solid #dbe1ea;
      border-top-color: #0f172a;
      border-radius: 50%;
      animation: spin 0.7s linear infinite;
      margin-bottom: 4px;
    }
    .phone-ring {
      width: 56px;
      height: 56px;
      border-radius: 50%;
      display: grid;
      place-items: center;
      background: #ecfdf3;
      color: #15803d;
      font-size: 24px;
      margin-bottom: 6px;
    }

    @keyframes spin {
      to { transform: rotate(360deg); }
    }
    @keyframes bell-tilt {
      0% { transform: rotate(-8deg); }
      100% { transform: rotate(8deg); }
    }
    @keyframes pulse-border {
      0% { box-shadow: 0 0 0 0 rgba(22, 163, 74, 0.4); }
      70% { box-shadow: 0 0 0 10px rgba(22, 163, 74, 0); }
      100% { box-shadow: 0 0 0 0 rgba(22, 163, 74, 0); }
    }

    @media (max-width: 640px) {
      .page-header, .incoming-top {
        align-items: flex-start;
        flex-direction: column;
      }
      .caller-grid {
        grid-template-columns: 1fr;
      }
      .call-actions {
        flex-direction: column-reverse;
      }
      .call-actions button {
        width: 100%;
      }
    }
  `]
})
export class IncomingCallComponent implements OnInit {
  private readonly realtime = inject(CallCenterRealtimeService);
  private readonly customers = inject(CustomerService);
  private readonly telephony = inject(TelephonyService);
  private readonly session = inject(IncomingCallSessionService);
  private readonly router = inject(Router);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly destroyRef = inject(DestroyRef);

  call: IncomingCallEvent | null = null;
  customer: CustomerSummary | null = null;
  loading = false;
  processing = false;
  action: 'accept' | 'reject' | null = null;
  errorMessage = '';

  // Simulation controls
  showSimPanel = false;
  simulating = false;
  simPhoneNumber = '8801712345678';

  get isRinging(): boolean {
    return this.call?.status.toLowerCase() === 'ringing';
  }

  ngOnInit(): void {
    this.realtime.connect();
    this.realtime.incomingCalls$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((event) => {
        if (event) this.prepareCall(event);
      });
  }

  toggleSimPanel(): void {
    this.showSimPanel = !this.showSimPanel;
  }

  triggerSimulatedCall(): void {
    if (!this.simPhoneNumber.trim()) return;
    this.simulating = true;
    this.errorMessage = '';

    this.telephony
      .simulateIncoming({
        phoneNumber: this.simPhoneNumber.trim(),
        correlationId: `SIM-${Date.now()}`
      })
      .pipe(
        finalize(() => {
          this.simulating = false;
          this.cdr.markForCheck();
        })
      )
      .subscribe({
        next: () => {
          this.showSimPanel = false;
          this.cdr.markForCheck();
        },
        error: (err) => {
          this.errorMessage = err?.error?.message || 'Unable to simulate incoming call.';
          this.cdr.markForCheck();
        }
      });
  }

  private prepareCall(event: IncomingCallEvent): void {
    const status = event.status.toLowerCase();
    // Accept both Queued and Ringing incoming calls
    if (status !== 'queued' && status !== 'ringing') return;

    this.call = event;
    this.customer = null;
    this.errorMessage = '';
    this.loading = true;

    const customerRequest: Observable<CustomerSummary | null> = event.customerId
      ? this.customers.getById(event.customerId)
      : of(null);

    customerRequest
      .pipe(
        finalize(() => {
          this.loading = false;
          this.cdr.markForCheck();
        })
      )
      .subscribe({
        next: (customer: CustomerSummary | null) => {
          this.customer = customer || {
            id: event.customerId || '',
            fullName: `Caller (${event.phoneNumber})`,
            phone: event.phoneNumber
          };
          this.cdr.markForCheck();
        },
        error: () => {
          // Graceful fallback for unknown customer
          this.customer = {
            id: event.customerId || '',
            fullName: `Caller (${event.phoneNumber})`,
            phone: event.phoneNumber
          };
          this.cdr.markForCheck();
        }
      });
  }

  accept(): void {
    if (!this.call || this.processing) return;
    this.processing = true;
    this.action = 'accept';
    this.errorMessage = '';

    this.telephony
      .acceptCall(this.call.callId)
      .pipe(
        finalize(() => {
          this.processing = false;
          this.action = null;
          this.cdr.markForCheck();
        })
      )
      .subscribe({
        next: () => {
          this.realtime.clearIncomingCall();
          const acceptedCall: IncomingCallEvent = {
            ...this.call!,
            status: 'Connected'
          };
          this.session.setSession({ event: acceptedCall, customer: this.customer });
          void this.router.navigate(['/active-call']);
        },
        error: (error) => {
          this.errorMessage = error?.error?.message || 'Unable to accept the call.';
          this.cdr.markForCheck();
        }
      });
  }

  reject(): void {
    if (!this.call || this.processing) return;
    this.processing = true;
    this.action = 'reject';
    this.errorMessage = '';

    this.telephony
      .rejectCall(this.call.callId)
      .pipe(
        finalize(() => {
          this.processing = false;
          this.action = null;
          this.cdr.markForCheck();
        })
      )
      .subscribe({
        next: () => {
          this.realtime.clearIncomingCall();
          this.call = null;
          this.customer = null;
          this.cdr.markForCheck();
        },
        error: (error) => {
          this.errorMessage = error?.error?.message || 'Unable to reject the call.';
          this.cdr.markForCheck();
        }
      });
  }

  statusClass(status: string): string {
    return status.toLowerCase();
  }
}
