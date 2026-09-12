import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap, catchError, of } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthResponse, AuthUser, LoginRequest } from './auth.models';

const ACCESS_TOKEN_KEY = 'falaq_access_token';
const REFRESH_TOKEN_KEY = 'falaq_refresh_token';
const AUTH_USER_KEY = 'falaq_auth_user';
const SESSION_ID_KEY = 'falaq_session_id';
const TOKEN_EXPIRY_KEY = 'falaq_token_expiry';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  readonly currentUser = signal<AuthUser | null>(this.readInitialUser());
  readonly isAuthenticated = computed(() => !!this.currentUser() && !this.isTokenExpired());
  readonly userRole = computed(() => this.currentUser()?.role ?? '');
  readonly userPermissions = computed(() => this.currentUser()?.permissions ?? []);

  login(request: LoginRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${environment.apiBaseUrl}/auth/login`, request).pipe(
      tap((response) => {
        this.storeSession(response);
      })
    );
  }

  logout(): void {
    const token = this.getAccessToken();
    if (token) {
      // Notify backend of logout (fire-and-forget)
      this.http.post(`${environment.apiBaseUrl}/auth/logout`, {}).pipe(
        catchError(() => of(null))
      ).subscribe();
    }

    this.clearStorage();
    this.currentUser.set(null);
  }

  getAccessToken(): string | null {
    if (this.isTokenExpired()) {
      this.clearStorage();
      this.currentUser.set(null);
      return null;
    }
    return sessionStorage.getItem(ACCESS_TOKEN_KEY);
  }

  getRefreshToken(): string | null {
    return localStorage.getItem(REFRESH_TOKEN_KEY);
  }

  getSessionId(): string | null {
    return sessionStorage.getItem(SESSION_ID_KEY);
  }

  getCurrentUser<T = AuthUser>(): T | null {
    return (this.currentUser() as unknown as T) ?? null;
  }

  hasRole(roles: string | string[]): boolean {
    const current = this.currentUser();
    if (!current) return false;
    if (current.role === 'Admin') return true;

    const allowed = Array.isArray(roles) ? roles : [roles];
    return allowed.some((r) => r.toLowerCase() === current.role.toLowerCase());
  }

  hasPermission(permission: string): boolean {
    const current = this.currentUser();
    if (!current) return false;
    if (current.role === 'Admin') return true;

    return current.permissions?.includes(permission) ?? false;
  }

  isTokenExpired(): boolean {
    const token = sessionStorage.getItem(ACCESS_TOKEN_KEY);
    if (!token) return true;

    const expiryString = sessionStorage.getItem(TOKEN_EXPIRY_KEY);
    if (expiryString) {
      const expiryDate = new Date(expiryString).getTime();
      if (Number.isFinite(expiryDate) && expiryDate <= Date.now()) {
        return true;
      }
    }

    // Secondary safety check: decode JWT exp claim
    try {
      const parts = token.split('.');
      if (parts.length === 3) {
        const payload = JSON.parse(atob(parts[1]));
        if (payload?.exp && typeof payload.exp === 'number') {
          return payload.exp * 1000 <= Date.now();
        }
      }
    } catch {
      return true;
    }

    return false;
  }

  private readInitialUser(): AuthUser | null {
    if (typeof window === 'undefined' || !window.sessionStorage) return null;
    if (this.isTokenExpired()) {
      this.clearStorage();
      return null;
    }
    const raw = sessionStorage.getItem(AUTH_USER_KEY);
    if (!raw) return null;
    try {
      return JSON.parse(raw) as AuthUser;
    } catch {
      return null;
    }
  }

  private clearStorage(): void {
    sessionStorage.removeItem(ACCESS_TOKEN_KEY);
    sessionStorage.removeItem(AUTH_USER_KEY);
    sessionStorage.removeItem(SESSION_ID_KEY);
    sessionStorage.removeItem(TOKEN_EXPIRY_KEY);
    localStorage.removeItem(REFRESH_TOKEN_KEY);
  }

  private storeSession(response: AuthResponse): void {
    sessionStorage.setItem(ACCESS_TOKEN_KEY, response.accessToken);
    sessionStorage.setItem(SESSION_ID_KEY, response.userId);
    if (response.expiresAtUtc) {
      sessionStorage.setItem(TOKEN_EXPIRY_KEY, response.expiresAtUtc);
    }

    const user: AuthUser = {
      id: response.userId,
      username: response.userName,
      role: response.role,
      permissions: response.permissions ?? []
    };

    sessionStorage.setItem(AUTH_USER_KEY, JSON.stringify(user));
    this.currentUser.set(user);
  }
}
