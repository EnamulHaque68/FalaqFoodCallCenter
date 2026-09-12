import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { SystemSettings } from '../models/settings.models';

@Injectable({ providedIn: 'root' })
export class SettingsService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiBaseUrl}/settings`;
  getAll(): Observable<SystemSettings> { return this.http.get<SystemSettings>(this.url); }
  update(settings: SystemSettings): Observable<SystemSettings> { return this.http.put<SystemSettings>(this.url, settings); }
  reset(): Observable<SystemSettings> { return this.http.post<SystemSettings>(`${this.url}/reset`, {}); }
}
