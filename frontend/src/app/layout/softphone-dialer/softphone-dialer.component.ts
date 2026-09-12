import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  DestroyRef,
  EventEmitter,
  Input,
  OnInit,
  Output,
  inject
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { TelephonyService } from '../../core/services/telephony.service';
import { TwilioVoiceService } from '../../core/services/twilio-voice.service';
import { IncomingCallSessionService } from '../../core/services/incoming-call-session.service';
import { CallDirection, CallStatus } from '../../core/models/agent-dashboard.models';

interface KeypadKey {
  digit: string;
  letters: string;
}

@Component({
  selector: 'app-softphone-dialer',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="softphone-container" [class.minimized]="isMinimized">
      <!-- Softphone Header -->
      <header class="dialer-header">
        <div class="header-title">
          <span class="phone-badge-icon">📞</span>
          <div>
            <strong>Falaq Softphone</strong>
            <div class="telephony-status">
              <span class="status-indicator-dot" [ngClass]="deviceStatusClass"></span>
              <span>{{ telephonyProviderName }} · {{ deviceState }}</span>
            </div>
          </div>
        </div>

        <div class="header-controls">
          <button
            type="button"
            class="icon-btn"
            [title]="isMinimized ? 'Expand Softphone' : 'Minimize Softphone'"
            (click)="toggleMinimize()">
            {{ isMinimized ? '▢' : '—' }}
          </button>
          <button
            type="button"
            class="icon-btn close-btn"
            title="Close Softphone"
            (click)="closeDialer()">
            ✕
          </button>
        </div>
      </header>

      @if (!isMinimized) {
        <div class="dialer-body">
          <!-- Number Input Display -->
          <div class="dialer-display">
            <div class="display-input-wrap">
              <input
                type="tel"
                class="phone-input"
                [(ngModel)]="phoneNumber"
                placeholder="Enter phone number…"
                (keydown.enter)="dialOutbound()"
                autocomplete="off" />
              @if (phoneNumber) {
                <button
                  type="button"
                  class="clear-input-btn"
                  title="Backspace"
                  (click)="backspace()">
                  ⌫
                </button>
              }
            </div>
            @if (phoneNumber) {
              <div class="display-hint">
                <span>International/Local format accepted</span>
                <button type="button" class="btn-clear-all" (click)="phoneNumber = ''">Clear</button>
              </div>
            }
          </div>

          <!-- Alert Messages -->
          @if (errorMessage) {
            <div class="dialer-alert error">
              <span>⚠️ {{ errorMessage }}</span>
              <button type="button" (click)="errorMessage = ''">✕</button>
            </div>
          }
          @if (successMessage) {
            <div class="dialer-alert success">
              <span>✅ {{ successMessage }}</span>
              <button type="button" (click)="successMessage = ''">✕</button>
            </div>
          }

          <!-- 12-Key DTMF Dialpad Grid -->
          <div class="keypad-grid">
            @for (key of keypadKeys; track key.digit) {
              <button
                type="button"
                class="keypad-btn"
                (click)="pressKey(key.digit)">
                <span class="key-digit">{{ key.digit }}</span>
                @if (key.letters) {
                  <span class="key-letters">{{ key.letters }}</span>
                }
              </button>
            }
          </div>

          <!-- Call Action Buttons -->
          <div class="dialer-actions">
            <button
              type="button"
              class="btn-call"
              [disabled]="calling || simulating || !phoneNumber.trim()"
              (click)="dialOutbound()">
              <span class="action-icon">📞</span>
              <span>{{ calling ? 'Connecting…' : 'Dial Outbound' }}</span>
            </button>

            <button
              type="button"
              class="btn-simulate"
              [disabled]="calling || simulating || !phoneNumber.trim()"
              title="Trigger a simulated incoming call to this softphone"
              (click)="simulateIncomingTest()">
              <span class="action-icon">⚡</span>
              <span>{{ simulating ? 'Ringing…' : 'Test Ring' }}</span>
            </button>
          </div>

          <!-- Quick Presets -->
          <div class="quick-presets">
            <span class="presets-label">Quick Dial Presets:</span>
            <div class="preset-chips">
              <button
                type="button"
                class="chip"
                (click)="setPreset('+8801712345678')">
                +8801712345678 (Demo)
              </button>
              <button
                type="button"
                class="chip"
                (click)="setPreset('+8801811223344')">
                +8801811223344 (Orders)
              </button>
              <button
                type="button"
                class="chip"
                (click)="setPreset('+8801999887766')">
                +8801999887766 (Support)
              </button>
            </div>
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    .softphone-container {
      position: fixed;
      bottom: 24px;
      right: 24px;
      width: 320px;
      background: #0d1628;
      border: 1px solid rgba(255, 255, 255, 0.12);
      border-radius: 16px;
      box-shadow: 0 20px 45px -10px rgba(0, 0, 0, 0.5), 0 0 0 1px rgba(255, 255, 255, 0.05);
      color: #e2e8f0;
      z-index: 1000;
      overflow: hidden;
      font-family: inherit;
      animation: slideUpFade 0.25s cubic-bezier(0.16, 1, 0.3, 1);
    }

    .softphone-container.minimized {
      width: 260px;
    }

    @keyframes slideUpFade {
      from {
        opacity: 0;
        transform: translateY(20px) scale(0.97);
      }
      to {
        opacity: 1;
        transform: translateY(0) scale(1);
      }
    }

    .dialer-header {
      padding: 12px 16px;
      background: #131f37;
      border-bottom: 1px solid rgba(255, 255, 255, 0.08);
      display: flex;
      justify-content: space-between;
      align-items: center;
    }

    .header-title {
      display: flex;
      align-items: center;
      gap: 10px;
    }

    .phone-badge-icon {
      font-size: 18px;
      background: rgba(34, 197, 94, 0.15);
      border: 1px solid rgba(34, 197, 94, 0.3);
      width: 32px;
      height: 32px;
      display: grid;
      place-items: center;
      border-radius: 8px;
    }

    .header-title strong {
      display: block;
      font-size: 13px;
      color: #fff;
    }

    .telephony-status {
      display: flex;
      align-items: center;
      gap: 6px;
      font-size: 11px;
      color: #94a3b8;
    }

    .status-indicator-dot {
      width: 7px;
      height: 7px;
      border-radius: 50%;
    }

    .status-ready {
      background: #22c55e;
      box-shadow: 0 0 8px rgba(34, 197, 94, 0.6);
    }

    .status-busy {
      background: #f59e0b;
      box-shadow: 0 0 8px rgba(245, 158, 11, 0.6);
    }

    .status-offline {
      background: #ef4444;
    }

    .header-controls {
      display: flex;
      align-items: center;
      gap: 4px;
    }

    .icon-btn {
      background: transparent;
      border: none;
      color: #94a3b8;
      cursor: pointer;
      width: 28px;
      height: 28px;
      border-radius: 6px;
      display: grid;
      place-items: center;
      font-size: 12px;
      transition: all 0.15s;
    }

    .icon-btn:hover {
      background: rgba(255, 255, 255, 0.1);
      color: #fff;
    }

    .close-btn:hover {
      background: rgba(239, 68, 68, 0.2);
      color: #ef4444;
    }

    .dialer-body {
      padding: 14px 16px 16px;
    }

    .dialer-display {
      background: #17243e;
      border: 1px solid rgba(255, 255, 255, 0.08);
      border-radius: 10px;
      padding: 8px 12px;
      margin-bottom: 12px;
    }

    .display-input-wrap {
      display: flex;
      align-items: center;
      gap: 6px;
    }

    .phone-input {
      flex: 1;
      background: transparent;
      border: none;
      color: #fff;
      font-size: 17px;
      font-weight: 600;
      letter-spacing: 0.03em;
      outline: none;
      width: 100%;
    }

    .phone-input::placeholder {
      color: #64748b;
      font-size: 14px;
      font-weight: 400;
    }

    .clear-input-btn {
      background: transparent;
      border: none;
      color: #94a3b8;
      cursor: pointer;
      font-size: 16px;
      padding: 2px 4px;
      border-radius: 4px;
    }

    .clear-input-btn:hover {
      color: #ef4444;
    }

    .display-hint {
      display: flex;
      justify-content: space-between;
      align-items: center;
      font-size: 10px;
      color: #64748b;
      margin-top: 4px;
      padding-top: 4px;
      border-top: 1px solid rgba(255, 255, 255, 0.04);
    }

    .btn-clear-all {
      background: transparent;
      border: none;
      color: #94a3b8;
      cursor: pointer;
      font-size: 10px;
      padding: 0;
    }

    .btn-clear-all:hover {
      color: #f87171;
    }

    .dialer-alert {
      padding: 8px 10px;
      border-radius: 6px;
      font-size: 11px;
      margin-bottom: 10px;
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 8px;
    }

    .dialer-alert.error {
      background: rgba(239, 68, 68, 0.15);
      border: 1px solid rgba(239, 68, 68, 0.3);
      color: #fca5a5;
    }

    .dialer-alert.success {
      background: rgba(34, 197, 94, 0.15);
      border: 1px solid rgba(34, 197, 94, 0.3);
      color: #86efac;
    }

    .dialer-alert button {
      background: transparent;
      border: none;
      color: inherit;
      cursor: pointer;
      font-size: 11px;
    }

    /* Keypad Grid */
    .keypad-grid {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 8px;
      margin-bottom: 14px;
    }

    .keypad-btn {
      background: #17243e;
      border: 1px solid rgba(255, 255, 255, 0.06);
      border-radius: 10px;
      padding: 8px 4px 6px;
      color: #f1f5f9;
      cursor: pointer;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      transition: all 0.1s ease;
      user-select: none;
    }

    .keypad-btn:hover {
      background: #213254;
      border-color: rgba(255, 255, 255, 0.15);
      transform: translateY(-1px);
    }

    .keypad-btn:active {
      background: #2c426e;
      transform: translateY(1px);
    }

    .key-digit {
      font-size: 18px;
      font-weight: 700;
      line-height: 1;
    }

    .key-letters {
      font-size: 9px;
      letter-spacing: 0.1em;
      color: #94a3b8;
      margin-top: 2px;
      font-weight: 600;
    }

    /* Call Actions */
    .dialer-actions {
      display: grid;
      grid-template-columns: 1.3fr 1fr;
      gap: 8px;
      margin-bottom: 12px;
    }

    .btn-call, .btn-simulate {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 6px;
      padding: 10px 8px;
      border-radius: 10px;
      font-size: 12px;
      font-weight: 700;
      cursor: pointer;
      border: none;
      transition: all 0.15s ease;
    }

    .btn-call {
      background: #16a34a;
      color: #fff;
      box-shadow: 0 4px 12px rgba(22, 163, 74, 0.35);
    }

    .btn-call:hover:not(:disabled) {
      background: #15803d;
      box-shadow: 0 4px 16px rgba(22, 163, 74, 0.5);
      transform: translateY(-1px);
    }

    .btn-simulate {
      background: #475569;
      color: #e2e8f0;
      border: 1px solid rgba(255, 255, 255, 0.1);
    }

    .btn-simulate:hover:not(:disabled) {
      background: #334155;
      color: #fff;
    }

    .btn-call:disabled, .btn-simulate:disabled {
      opacity: 0.5;
      cursor: not-allowed;
      transform: none;
    }

    /* Quick Presets */
    .quick-presets {
      padding-top: 8px;
      border-top: 1px solid rgba(255, 255, 255, 0.06);
    }

    .presets-label {
      display: block;
      font-size: 10px;
      text-transform: uppercase;
      letter-spacing: 0.08em;
      color: #64748b;
      margin-bottom: 6px;
    }

    .preset-chips {
      display: flex;
      flex-wrap: wrap;
      gap: 4px;
    }

    .chip {
      background: rgba(255, 255, 255, 0.05);
      border: 1px solid rgba(255, 255, 255, 0.08);
      border-radius: 6px;
      padding: 3px 6px;
      font-size: 10px;
      color: #94a3b8;
      cursor: pointer;
      transition: all 0.12s;
    }

    .chip:hover {
      background: rgba(255, 255, 255, 0.12);
      color: #fff;
      border-color: rgba(255, 255, 255, 0.2);
    }
  `]
})
export class SoftphoneDialerComponent implements OnInit {
  private readonly telephonyService = inject(TelephonyService);
  private readonly twilioVoiceService = inject(TwilioVoiceService);
  private readonly sessionService = inject(IncomingCallSessionService);
  private readonly router = inject(Router);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly destroyRef = inject(DestroyRef);

  @Input() isOpen = false;
  @Output() close = new EventEmitter<void>();

  phoneNumber = '';
  isMinimized = false;
  calling = false;
  simulating = false;
  errorMessage = '';
  successMessage = '';

  deviceState = 'Ready';
  telephonyProviderName = 'Telephony';

  private audioCtx: AudioContext | null = null;

  readonly keypadKeys: KeypadKey[] = [
    { digit: '1', letters: '' },
    { digit: '2', letters: 'ABC' },
    { digit: '3', letters: 'DEF' },
    { digit: '4', letters: 'GHI' },
    { digit: '5', letters: 'JKL' },
    { digit: '6', letters: 'MNO' },
    { digit: '7', letters: 'PQRS' },
    { digit: '8', letters: 'TUV' },
    { digit: '9', letters: 'WXYZ' },
    { digit: '*', letters: '' },
    { digit: '0', letters: '+' },
    { digit: '#', letters: '' }
  ];

  ngOnInit(): void {
    this.twilioVoiceService.deviceState$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(state => {
        this.deviceState = state || 'Ready';
        this.cdr.markForCheck();
      });

    this.twilioVoiceService.currentProvider$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(prov => {
        this.telephonyProviderName = prov || 'Softphone';
        this.cdr.markForCheck();
      });
  }

  get deviceStatusClass(): string {
    if (this.deviceState === 'registered' || this.deviceState === 'ready' || this.deviceState === 'Ready') {
      return 'status-ready';
    }
    if (this.deviceState === 'busy' || this.deviceState === 'connect') {
      return 'status-busy';
    }
    return 'status-ready';
  }

  pressKey(digit: string): void {
    this.phoneNumber += digit;
    this.playTone(digit);
    this.cdr.markForCheck();
  }

  backspace(): void {
    if (this.phoneNumber.length > 0) {
      this.phoneNumber = this.phoneNumber.slice(0, -1);
      this.cdr.markForCheck();
    }
  }

  setPreset(num: string): void {
    this.phoneNumber = num;
    this.cdr.markForCheck();
  }

  toggleMinimize(): void {
    this.isMinimized = !this.isMinimized;
    this.cdr.markForCheck();
  }

  closeDialer(): void {
    this.close.emit();
  }

  dialOutbound(): void {
    const rawNumber = this.phoneNumber.trim();
    if (!rawNumber || this.calling) return;

    this.calling = true;
    this.errorMessage = '';
    this.successMessage = '';
    this.cdr.markForCheck();

    const correlationId = `OUT-${Date.now()}`;

    this.telephonyService.initiateOutbound({
      phoneNumber: rawNumber,
      correlationId
    })
      .pipe(
        finalize(() => {
          this.calling = false;
          this.cdr.markForCheck();
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: call => {
          this.sessionService.setSession({
            event: {
              callId: call.callId,
              customerId: call.customerId ?? null,
              phoneNumber: call.phoneNumber,
              direction: 'Outbound',
              status: 'Ringing',
              occurredAtUtc: new Date().toISOString()
            },
            customer: {
              id: call.customerId || '',
              fullName: 'Outbound Contact',
              phone: call.phoneNumber
            }
          });

          // Softphone Device call
          this.twilioVoiceService.makeCall(rawNumber);

          this.successMessage = 'Call connected! Redirecting to workspace…';
          this.cdr.markForCheck();

          setTimeout(() => {
            this.router.navigate(['/active-call'], { queryParams: { callId: call.callId } });
            this.closeDialer();
          }, 400);
        },
        error: err => {
          this.errorMessage = err?.error?.message || err?.message || 'Failed to place outbound call.';
          this.cdr.markForCheck();
        }
      });
  }

  simulateIncomingTest(): void {
    const rawNumber = this.phoneNumber.trim();
    if (!rawNumber || this.simulating) return;

    this.simulating = true;
    this.errorMessage = '';
    this.successMessage = '';
    this.cdr.markForCheck();

    this.telephonyService.simulateIncoming({
      phoneNumber: rawNumber,
      correlationId: `SIM-${Date.now()}`
    })
      .pipe(
        finalize(() => {
          this.simulating = false;
          this.cdr.markForCheck();
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: call => {
          this.successMessage = `Incoming call simulated from ${rawNumber}! Answer alert dispatched.`;
          this.cdr.markForCheck();
        },
        error: err => {
          this.errorMessage = err?.error?.message || err?.message || 'Simulation failed.';
          this.cdr.markForCheck();
        }
      });
  }

  private playTone(digit: string): void {
    try {
      if (!this.audioCtx) {
        const AudioContextClass = window.AudioContext || (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext;
        if (AudioContextClass) {
          this.audioCtx = new AudioContextClass();
        }
      }
      if (this.audioCtx && this.audioCtx.state === 'suspended') {
        this.audioCtx.resume();
      }
      if (this.audioCtx) {
        const osc = this.audioCtx.createOscillator();
        const gain = this.audioCtx.createGain();
        osc.type = 'sine';
        // Gentle pleasant tone frequency
        const freqs: Record<string, number> = {
          '1': 697, '2': 770, '3': 852,
          '4': 697, '5': 770, '6': 852,
          '7': 697, '8': 770, '9': 852,
          '*': 941, '0': 941, '#': 941
        };
        osc.frequency.value = freqs[digit] || 750;
        gain.gain.setValueAtTime(0.05, this.audioCtx.currentTime);
        gain.gain.exponentialRampToValueAtTime(0.0001, this.audioCtx.currentTime + 0.08);
        osc.connect(gain);
        gain.connect(this.audioCtx.destination);
        osc.start();
        osc.stop(this.audioCtx.currentTime + 0.08);
      }
    } catch {
      // AudioContext optional
    }
  }
}
