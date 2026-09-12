import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  CallQueue,
  QueueSummary,
  CallQueueEntry,
  CreateQueueRequest,
  UpdateQueueRequest
} from '../models/queue.models';

@Injectable({ providedIn: 'root' })
export class QueueService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/queues`;

  getQueues(): Observable<CallQueue[]> {
    return this.http.get<CallQueue[]>(this.baseUrl);
  }

  getSummary(): Observable<QueueSummary> {
    return this.http.get<QueueSummary>(`${this.baseUrl}/summary`);
  }

  getQueueById(id: string): Observable<CallQueue> {
    return this.http.get<CallQueue>(`${this.baseUrl}/${id}`);
  }

  getQueueEntries(queueId: string): Observable<CallQueueEntry[]> {
    return this.http.get<CallQueueEntry[]>(`${this.baseUrl}/${queueId}/entries`);
  }

  createQueue(request: CreateQueueRequest): Observable<CallQueue> {
    return this.http.post<CallQueue>(this.baseUrl, request);
  }

  updateQueue(id: string, request: UpdateQueueRequest): Observable<CallQueue> {
    return this.http.put<CallQueue>(`${this.baseUrl}/${id}`, request);
  }

  deleteQueue(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }

  prioritizeEntry(entryId: string, priority: number): Observable<CallQueueEntry> {
    return this.http.post<CallQueueEntry>(`${this.baseUrl}/entries/${entryId}/priority`, { priority });
  }

  cancelCall(callId: string, reason?: string): Observable<{ message: string }> {
    let params = new HttpParams();
    if (reason) {
      params = params.set('reason', reason);
    }
    return this.http.post<{ message: string }>(`${this.baseUrl}/calls/${callId}/cancel`, {}, { params });
  }

  completeCall(callId: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.baseUrl}/calls/${callId}/complete`, {});
  }

  triggerAutoAssign(agentId?: string): Observable<unknown> {
    let params = new HttpParams();
    if (agentId) {
      params = params.set('agentId', agentId);
    }
    return this.http.post<unknown>(`${this.baseUrl}/auto-assign`, {}, { params });
  }

  assignCallDirectly(callId: string, agentId: string): Observable<unknown> {
    return this.http.post<unknown>(`${environment.apiBaseUrl}/routing/calls/${callId}/assign`, { agentId });
  }
}
