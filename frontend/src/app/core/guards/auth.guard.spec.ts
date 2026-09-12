import { TestBed } from '@angular/core/testing';
import { Router, ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { authGuard, roleGuard } from './auth.guard';
import { AuthService } from '../auth/auth.service';

describe('AuthGuards', () => {
  let authServiceMock: jasmine.SpyObj<AuthService>;
  let routerMock: jasmine.SpyObj<Router>;

  beforeEach(() => {
    authServiceMock = jasmine.createSpyObj<AuthService>('AuthService', [
      'isAuthenticated',
      'isTokenExpired',
      'logout',
      'hasRole',
      'hasPermission'
    ]);

    routerMock = jasmine.createSpyObj<Router>('Router', ['createUrlTree']);

    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: authServiceMock },
        { provide: Router, useValue: routerMock }
      ]
    });
  });

  describe('authGuard', () => {
    it('should allow access when user is authenticated and token is valid', () => {
      authServiceMock.isAuthenticated.and.returnValue(true);
      authServiceMock.isTokenExpired.and.returnValue(false);

      const result = TestBed.runInInjectionContext(() =>
        authGuard({} as ActivatedRouteSnapshot, { url: '/dashboard' } as RouterStateSnapshot)
      );

      expect(result).toBeTrue();
    });

    it('should redirect to /login when user is unauthenticated', () => {
      authServiceMock.isAuthenticated.and.returnValue(false);
      authServiceMock.isTokenExpired.and.returnValue(true);
      const dummyTree = {} as any;
      routerMock.createUrlTree.and.returnValue(dummyTree);

      const result = TestBed.runInInjectionContext(() =>
        authGuard({} as ActivatedRouteSnapshot, { url: '/dashboard' } as RouterStateSnapshot)
      );

      expect(authServiceMock.logout).toHaveBeenCalled();
      expect(routerMock.createUrlTree).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/dashboard' } });
      expect(result).toBe(dummyTree);
    });
  });

  describe('roleGuard', () => {
    it('should redirect to /unauthorized when user lacks expected role', () => {
      authServiceMock.isAuthenticated.and.returnValue(true);
      authServiceMock.isTokenExpired.and.returnValue(false);
      authServiceMock.hasRole.and.returnValue(false);

      const dummyTree = {} as any;
      routerMock.createUrlTree.and.returnValue(dummyTree);

      const routeSnapshot = { data: { roles: ['Admin'] } } as unknown as ActivatedRouteSnapshot;

      const result = TestBed.runInInjectionContext(() => roleGuard(routeSnapshot, {} as RouterStateSnapshot));

      expect(routerMock.createUrlTree).toHaveBeenCalledWith(['/unauthorized']);
      expect(result).toBe(dummyTree);
    });

    it('should allow access when user has expected role and permissions', () => {
      authServiceMock.isAuthenticated.and.returnValue(true);
      authServiceMock.isTokenExpired.and.returnValue(false);
      authServiceMock.hasRole.and.returnValue(true);
      authServiceMock.hasPermission.and.returnValue(true);

      const routeSnapshot = {
        data: { roles: ['Supervisor'], permissions: ['reports.view'] }
      } as unknown as ActivatedRouteSnapshot;

      const result = TestBed.runInInjectionContext(() => roleGuard(routeSnapshot, {} as RouterStateSnapshot));

      expect(result).toBeTrue();
    });
  });
});
