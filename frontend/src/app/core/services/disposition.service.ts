import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CallDisposition, CreateDispositionRequest, UpdateDispositionRequest } from '../models/disposition.models';

@Injectable({ providedIn: 'root' })
export class DispositionService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/dispositions`;

  getDispositions(includeInactive = false): Observable<CallDisposition[]> {
    return this.http.get<CallDisposition[]>(this.baseUrl, {
      params: { includeInactive: includeInactive.toString() }
    });
  }

  getById(id: string): Observable<CallDisposition> {
    return this.http.get<CallDisposition>(`${this.baseUrl}/${id}`);
  }

  create(request: CreateDispositionRequest): Observable<CallDisposition> {
    return this.http.post<CallDisposition>(this.baseUrl, request);
  }

  update(id: string, request: UpdateDispositionRequest): Observable<CallDisposition> {
    return this.http.put<CallDisposition>(`${this.baseUrl}/${id}`, request);
  }

  toggleStatus(id: string): Observable<CallDisposition> {
    return this.http.patch<CallDisposition>(`${this.baseUrl}/${id}/toggle-status`, {});
  }
}
