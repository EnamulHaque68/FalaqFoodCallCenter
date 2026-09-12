import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { EligibleAgent, TelephonyCallResponse, TelephonyTokenResponse, TransferCallRequest, TransferType } from '../models/incoming-call.models';
import { CompleteCallRequest } from '../models/disposition.models';

@Injectable({ providedIn: 'root' })
export class TelephonyService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/telephony`;

  getVoiceToken(): Observable<TelephonyTokenResponse> {
    return this.http.get<TelephonyTokenResponse>(`${this.baseUrl}/token`);
  }

  getEligibleTransferAgents(callId: string, transferType: TransferType = TransferType.Blind): Observable<EligibleAgent[]> {
    return this.http.get<EligibleAgent[]>(`${this.baseUrl}/calls/${callId}/transfer/eligible-agents`, {
      params: { transferType: transferType.toString() }
    });
  }

  acceptCall(callId: string): Observable<TelephonyCallResponse> {
    return this.http.post<TelephonyCallResponse>(`${this.baseUrl}/calls/${callId}/accept`, {});
  }

  rejectCall(callId: string): Observable<TelephonyCallResponse> {
    return this.http.post<TelephonyCallResponse>(`${this.baseUrl}/calls/${callId}/reject`, {});
  }

  holdCall(callId: string): Observable<TelephonyCallResponse> {
    return this.http.post<TelephonyCallResponse>(`${this.baseUrl}/calls/${callId}/hold`, {});
  }

  resumeCall(callId: string): Observable<TelephonyCallResponse> {
    return this.http.post<TelephonyCallResponse>(`${this.baseUrl}/calls/${callId}/resume`, {});
  }

  transferCall(callId: string, request: TransferCallRequest): Observable<TelephonyCallResponse> {
    return this.http.post<TelephonyCallResponse>(`${this.baseUrl}/calls/${callId}/transfer`, request);
  }

  simulateIncoming(request: { phoneNumber: string; customerId?: string | null; correlationId?: string }): Observable<TelephonyCallResponse> {
    return this.http.post<TelephonyCallResponse>(`${this.baseUrl}/incoming/simulate`, {
      phoneNumber: request.phoneNumber,
      customerId: request.customerId ?? null,
      correlationId: request.correlationId || `SIM-${Date.now()}`
    });
  }

  initiateOutbound(request: { phoneNumber: string; customerId?: string | null; correlationId?: string }): Observable<TelephonyCallResponse> {
    return this.http.post<TelephonyCallResponse>(`${this.baseUrl}/outgoing`, {
      phoneNumber: request.phoneNumber,
      customerId: request.customerId ?? null,
      correlationId: request.correlationId || `OUT-${Date.now()}`
    });
  }

  endCall(callId: string): Observable<TelephonyCallResponse> {
    return this.http.post<TelephonyCallResponse>(`${this.baseUrl}/calls/${callId}/end`, {});
  }

  completeCall(callId: string, request: CompleteCallRequest): Observable<TelephonyCallResponse> {
    return this.http.post<TelephonyCallResponse>(`${this.baseUrl}/calls/${callId}/complete`, request);
  }
}
