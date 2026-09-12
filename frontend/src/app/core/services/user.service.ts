import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  UserListItem,
  CreateUserRequest,
  UpdateUserRequest,
  ResetPasswordRequest,
  ChangePasswordRequest,
  RoleItem,
  UserFilter
} from '../models/user.models';

@Injectable({ providedIn: 'root' })
export class UserService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/users`;

  getAll(filter?: UserFilter): Observable<UserListItem[]> {
    let params = new HttpParams();
    if (filter) {
      if (filter.search?.trim()) {
        params = params.set('search', filter.search.trim());
      }
      if (filter.role?.trim()) {
        params = params.set('role', filter.role.trim());
      }
      if (filter.isActive !== undefined && filter.isActive !== '') {
        params = params.set('isActive', String(filter.isActive));
      }
    }
    return this.http.get<UserListItem[]>(this.baseUrl, { params });
  }

  getById(id: string): Observable<UserListItem> {
    return this.http.get<UserListItem>(`${this.baseUrl}/${id}`);
  }

  getRoles(): Observable<RoleItem[]> {
    return this.http.get<RoleItem[]>(`${this.baseUrl}/roles`);
  }

  create(request: CreateUserRequest): Observable<UserListItem> {
    return this.http.post<UserListItem>(this.baseUrl, request);
  }

  update(id: string, request: UpdateUserRequest): Observable<UserListItem> {
    return this.http.put<UserListItem>(`${this.baseUrl}/${id}`, request);
  }

  deactivate(id: string): Observable<UserListItem> {
    return this.http.put<UserListItem>(`${this.baseUrl}/${id}/deactivate`, null);
  }

  reactivate(id: string): Observable<UserListItem> {
    return this.http.put<UserListItem>(`${this.baseUrl}/${id}/reactivate`, null);
  }

  resetPassword(id: string, request: ResetPasswordRequest): Observable<UserListItem> {
    return this.http.post<UserListItem>(`${this.baseUrl}/${id}/reset-password`, request);
  }

  changePassword(id: string, request: ChangePasswordRequest): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.baseUrl}/${id}/change-password`, request);
  }
}
