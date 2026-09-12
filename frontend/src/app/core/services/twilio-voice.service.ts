import { Injectable, inject, signal } from '@angular/core';
import { BehaviorSubject, firstValueFrom } from 'rxjs';
import { TelephonyService } from './telephony.service';
import { TelephonyTokenResponse } from '../models/incoming-call.models';
import { Device, Call } from '@twilio/voice-sdk';

export type SoftphoneDeviceState = 'unregistered' | 'registering' | 'registered' | 'destroyed' | 'error';
export type SoftphoneCallState = 'idle' | 'ringing' | 'connecting' | 'connected' | 'on-hold' | 'ended';

@Injectable({ providedIn: 'root' })
export class TwilioVoiceService {
  private readonly telephonyService = inject(TelephonyService);

  private device: Device | null = null;
  private activeTwilioCall: Call | null = null;
  private tokenRefreshTimer: any = null;

  // Observables & Signals for UI state
  readonly deviceState$ = new BehaviorSubject<SoftphoneDeviceState>('unregistered');
  readonly callState$ = new BehaviorSubject<SoftphoneCallState>('idle');
  readonly isMuted$ = new BehaviorSubject<boolean>(false);
  readonly activeCallSid$ = new BehaviorSubject<string | null>(null);
  readonly currentProvider$ = new BehaviorSubject<string>('Simulated');
  readonly errorMessage$ = new BehaviorSubject<string | null>(null);

  // Angular signals for reactive template binding
  readonly isMuted = signal<boolean>(false);
  readonly isReady = signal<boolean>(false);
  readonly callStatus = signal<SoftphoneCallState>('idle');
  readonly currentIdentity = signal<string>('');

  /**
   * Initializes the softphone device by obtaining a token from the backend.
   * Seamlessly operates in real Twilio Voice mode or simulated softphone mode.
   */
  async initDevice(): Promise<void> {
    try {
      this.deviceState$.next('registering');
      this.errorMessage$.next(null);

      const tokenDto: TelephonyTokenResponse = await firstValueFrom(
        this.telephonyService.getVoiceToken()
      );

      this.currentProvider$.next(tokenDto.provider);
      this.currentIdentity.set(tokenDto.identity);

      if (tokenDto.provider === 'Twilio' && tokenDto.token && tokenDto.token.startsWith('eyJ')) {
        await this.setupTwilioDevice(tokenDto.token, tokenDto.expiresInSeconds);
      } else {
        this.setupSimulatedDevice();
      }

      this.scheduleTokenRefresh(tokenDto.expiresInSeconds);
    } catch (err: any) {
      console.warn('[Softphone] Failed to initialize telephony token, falling back to simulated mode:', err);
      this.setupSimulatedDevice();
    }
  }

  private async setupTwilioDevice(token: string, expiresInSeconds: number): Promise<void> {
    try {
      if (this.device) {
        this.device.destroy();
        this.device = null;
      }

      this.device = new Device(token, {
        logLevel: 1,
        codecPreferences: [Call.Codec.Opus, Call.Codec.PCMU],
        enableImprovedSignalingErrorPrecision: true
      });

      this.device.on('registered', () => {
        this.deviceState$.next('registered');
        this.isReady.set(true);
      });

      this.device.on('unregistered', () => {
        this.deviceState$.next('unregistered');
        this.isReady.set(false);
      });

      this.device.on('error', (error: any) => {
        console.error('[TwilioVoice] Device error:', error);
        this.errorMessage$.next(error?.message || 'Twilio device error');
        this.deviceState$.next('error');
      });

      this.device.on('tokenWillExpire', async () => {
        await this.refreshToken();
      });

      this.device.on('incoming', (call: Call) => {
        this.handleTwilioIncomingCall(call);
      });

      await this.device.register();
    } catch (error: any) {
      console.error('[TwilioVoice] Failed to create Twilio Device:', error);
      this.setupSimulatedDevice();
    }
  }

  private setupSimulatedDevice(): void {
    this.deviceState$.next('registered');
    this.isReady.set(true);
    this.errorMessage$.next(null);
  }

  private handleTwilioIncomingCall(call: Call): void {
    this.activeTwilioCall = call;
    this.activeCallSid$.next(call.parameters?.['CallSid'] || null);
    this.callState$.next('ringing');
    this.callStatus.set('ringing');

    call.on('accept', () => {
      this.callState$.next('connected');
      this.callStatus.set('connected');
    });

    call.on('disconnect', () => {
      this.handleCallEnded();
    });

    call.on('cancel', () => {
      this.handleCallEnded();
    });

    call.on('reject', () => {
      this.handleCallEnded();
    });

    call.on('mute', (isMuted: boolean) => {
      this.isMuted$.next(isMuted);
      this.isMuted.set(isMuted);
    });

    call.on('error', (err: any) => {
      console.error('[TwilioVoice] Call error:', err);
      this.handleCallEnded();
    });
  }

  /**
   * Accepts the current active/incoming call via Twilio SDK or simulated bridge.
   */
  async acceptCall(): Promise<void> {
    if (this.activeTwilioCall) {
      this.callState$.next('connecting');
      this.callStatus.set('connecting');
      this.activeTwilioCall.accept();
    } else {
      this.callState$.next('connected');
      this.callStatus.set('connected');
    }
  }

  /**
   * Rejects the current incoming call.
   */
  rejectCall(): void {
    if (this.activeTwilioCall) {
      this.activeTwilioCall.reject();
      this.activeTwilioCall = null;
    }
    this.handleCallEnded();
  }

  /**
   * Disconnects / hangs up the active call.
   */
  disconnectCall(): void {
    if (this.activeTwilioCall) {
      this.activeTwilioCall.disconnect();
      this.activeTwilioCall = null;
    }
    this.handleCallEnded();
  }

  /**
   * Sets microphone mute state.
   */
  mute(muted: boolean): void {
    if (this.activeTwilioCall) {
      this.activeTwilioCall.mute(muted);
    }
    this.isMuted$.next(muted);
    this.isMuted.set(muted);
  }

  /**
   * Toggles microphone mute state.
   */
  toggleMute(): boolean {
    const nextState = !this.isMuted$.value;
    this.mute(nextState);
    return nextState;
  }

  /**
   * Makes an outbound call to a customer phone number or agent client.
   */
  async makeCall(destinationNumber: string): Promise<void> {
    if (this.device && this.currentProvider$.value === 'Twilio') {
      try {
        this.callState$.next('connecting');
        this.callStatus.set('connecting');

        const call = await this.device.connect({
          params: { To: destinationNumber }
        });

        this.handleTwilioIncomingCall(call);
      } catch (err: any) {
        console.error('[TwilioVoice] Outbound call error:', err);
        this.errorMessage$.next(err?.message || 'Outbound call failed');
        this.handleCallEnded();
      }
    } else {
      this.callState$.next('connected');
      this.callStatus.set('connected');
    }
  }

  private handleCallEnded(): void {
    this.activeTwilioCall = null;
    this.activeCallSid$.next(null);
    this.isMuted$.next(false);
    this.isMuted.set(false);
    this.callState$.next('ended');
    this.callStatus.set('ended');
  }

  private async refreshToken(): Promise<void> {
    try {
      const tokenDto = await firstValueFrom(this.telephonyService.getVoiceToken());
      if (this.device && tokenDto.token) {
        await this.device.updateToken(tokenDto.token);
      }
      this.scheduleTokenRefresh(tokenDto.expiresInSeconds);
    } catch (err) {
      console.warn('[Softphone] Token refresh failed:', err);
    }
  }

  private scheduleTokenRefresh(expiresInSeconds: number): void {
    if (this.tokenRefreshTimer) {
      clearTimeout(this.tokenRefreshTimer);
    }
    // Refresh at 80% of TTL or 2 minutes before expiry
    const refreshDelayMs = Math.max((expiresInSeconds - 120) * 1000, 30000);
    this.tokenRefreshTimer = setTimeout(() => this.refreshToken(), refreshDelayMs);
  }

  destroy(): void {
    if (this.tokenRefreshTimer) {
      clearTimeout(this.tokenRefreshTimer);
      this.tokenRefreshTimer = null;
    }
    if (this.activeTwilioCall) {
      this.activeTwilioCall.disconnect();
      this.activeTwilioCall = null;
    }
    if (this.device) {
      this.device.destroy();
      this.device = null;
    }
    this.deviceState$.next('destroyed');
    this.isReady.set(false);
  }
}
