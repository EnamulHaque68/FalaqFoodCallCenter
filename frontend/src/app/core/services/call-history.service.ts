import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CallHistoryPagedResult, CallHistoryQuery, CallHistoryItem, CallTimelineEvent } from '../models/call-history.models';

@Injectable({ providedIn: 'root' })
export class CallHistoryService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/calls`;

  getHistory(query: CallHistoryQuery): Observable<CallHistoryPagedResult> {
    let params = new HttpParams()
      .set('page', query.page)
      .set('pageSize', query.pageSize);
    if (query.search?.trim()) params = params.set('search', query.search.trim());
    if (query.customerId) params = params.set('customerId', query.customerId);
    if (query.agentId) params = params.set('agentId', query.agentId);
    if (query.direction !== undefined) params = params.set('direction', query.direction);
    if (query.status !== undefined) params = params.set('status', query.status);
    if (query.fromUtc) params = params.set('fromUtc', query.fromUtc);
    if (query.toUtc) params = params.set('toUtc', query.toUtc);
    if (query.dispositionId) params = params.set('dispositionId', query.dispositionId);
    return this.http.get<CallHistoryPagedResult>(`${this.baseUrl}/history`, { params });
  }

  getById(id: string): Observable<CallHistoryItem> {
    return this.http.get<CallHistoryItem>(`${this.baseUrl}/${id}`);
  }

  getTimeline(callId: string): Observable<CallTimelineEvent[]> {
    return this.http.get<CallTimelineEvent[]>(`${this.baseUrl}/${callId}/timeline`);
  }
}
