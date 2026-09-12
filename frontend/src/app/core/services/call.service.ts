import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CallStatus, CallDirection } from '../models/agent-dashboard.models';

import { CallTimelineEvent } from '../models/incoming-call.models';

export interface CreateOutgoingCallRequest {
  customerId: string;
  phoneNumber: string;
  correlationId: string;
}

export interface CallResponse {
  id: string;
  customerId: string;
  assignedAgentId?: string | null;
  callQueueId?: string | null;
  callDispositionId?: string | null;
  providerCallId?: string | null;
  correlationId: string;
  direction: CallDirection | string | number;
  status: CallStatus | string | number;
  phoneNumber: string;
  notes?: string | null;
  startedAt: string;
  answeredAt?: string | null;
  endedAt?: string | null;
  createdAt: string;
  updatedAt?: string | null;
}

@Injectable({ providedIn: 'root' })
export class CallService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/calls`;

  createOutgoing(request: CreateOutgoingCallRequest): Observable<CallResponse> {
    return this.http.post<CallResponse>(`${this.baseUrl}/outgoing`, request, {
      headers: { 'Idempotency-Key': crypto.randomUUID() }
    });
  }

  getById(callId: string): Observable<CallResponse> {
    return this.http.get<CallResponse>(`${this.baseUrl}/${callId}`);
  }

  getTimeline(callId: string): Observable<CallTimelineEvent[]> {
    return this.http.get<CallTimelineEvent[]>(`${this.baseUrl}/${callId}/timeline`);
  }

  updateNotes(callId: string, notes: string): Observable<CallResponse> {
    return this.http.put<CallResponse>(`${this.baseUrl}/${callId}/notes`, { notes });
  }

  complete(callId: string, dispositionId: string, notes?: string): Observable<CallResponse> {
    return this.http.post<CallResponse>(`${this.baseUrl}/${callId}/complete`, { dispositionId, notes });
  }
}
