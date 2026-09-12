import { inject } from '@angular/core';
import { ActivatedRouteSnapshot, CanActivateFn, Router, RouterStateSnapshot } from '@angular/router';
import { AuthService } from '../auth/auth.service';

export const authGuard: CanActivateFn = (_route: ActivatedRouteSnapshot, state: RouterStateSnapshot) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated() && !auth.isTokenExpired()) {
    return true;
  }

  // Clear expired/stale tokens
  auth.logout();
  return router.createUrlTree(['/login'], {
    queryParams: { returnUrl: state.url }
  });
};

export const roleGuard: CanActivateFn = (route: ActivatedRouteSnapshot) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (!auth.isAuthenticated() || auth.isTokenExpired()) {
    auth.logout();
    return router.createUrlTree(['/login']);
  }

  const expectedRoles = route.data['roles'] as string[] | undefined;
  if (expectedRoles && expectedRoles.length > 0) {
    const hasRole = expectedRoles.some((role) => auth.hasRole(role));
    if (!hasRole) {
      return router.createUrlTree(['/unauthorized']);
    }
  }

  const expectedPermissions = route.data['permissions'] as string[] | undefined;
  if (expectedPermissions && expectedPermissions.length > 0) {
    const hasPermission = expectedPermissions.every((perm) => auth.hasPermission(perm));
    if (!hasPermission) {
      return router.createUrlTree(['/unauthorized']);
    }
  }

  return true;
};
