import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { AuthService } from './auth.service';
import { environment } from '../../../environments/environment';
import { AuthResponse } from './auth.models';

describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();
    localStorage.clear();

    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [AuthService]
    });

    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    sessionStorage.clear();
    localStorage.clear();
  });

  it('should be created and initially unauthenticated', () => {
    expect(service).toBeTruthy();
    expect(service.currentUser()).toBeNull();
    expect(service.isAuthenticated()).toBeFalse();
  });

  it('login should issue POST request, store session, and update signals', () => {
    const mockResponse: AuthResponse = {
      accessToken: 'header.' + btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + 3600 })) + '.signature',
      expiresAtUtc: new Date(Date.now() + 3600000).toISOString(),
      userId: '1234',
      userName: 'agent1',
      role: 'Agent',
      permissions: ['calls.view', 'calls.manage']
    };

    service.login({ username: 'agent1', password: 'Password123!' }).subscribe((res) => {
      expect(res.accessToken).toEqual(mockResponse.accessToken);
    });

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/auth/login`);
    expect(req.request.method).toBe('POST');
    req.flush(mockResponse);

    expect(service.isAuthenticated()).toBeTrue();
    expect(service.userRole()).toBe('Agent');
    expect(service.hasPermission('calls.view')).toBeTrue();
    expect(service.hasPermission('admin.delete')).toBeFalse();
  });

  it('hasRole should return true for matching role and for Admin', () => {
    const adminResponse: AuthResponse = {
      accessToken: 'header.' + btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + 3600 })) + '.signature',
      expiresAtUtc: new Date(Date.now() + 3600000).toISOString(),
      userId: 'admin-id',
      userName: 'admin',
      role: 'Admin',
      permissions: ['all']
    };

    service.login({ username: 'admin', password: 'Password123!' }).subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/auth/login`);
    req.flush(adminResponse);

    expect(service.hasRole('Agent')).toBeTrue(); // Admin has all roles
    expect(service.hasRole('Supervisor')).toBeTrue();
  });

  it('logout should clear storage and notify server', () => {
    sessionStorage.setItem('falaq_access_token', 'mock-token');

    service.logout();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/auth/logout`);
    expect(req.request.method).toBe('POST');
    req.flush({});

    expect(service.currentUser()).toBeNull();
    expect(service.isAuthenticated()).toBeFalse();
  });
});
