import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';

@Component({
  selector: 'app-unauthorized',
  standalone: true,
  imports: [RouterLink],
  template: `
    <main class="unauthorized-page" style="display: flex; min-height: 80vh; align-items: center; justify-content: center; padding: 2rem;">
      <div class="card" style="max-width: 540px; width: 100%; text-align: center; border: 1px solid var(--border, #e2e8f0); border-radius: 12px; padding: 2.5rem; background: var(--card-bg, #ffffff); box-shadow: 0 10px 25px -5px rgba(0, 0, 0, 0.05);">
        <div style="font-size: 3.5rem; line-height: 1; margin-bottom: 1rem;">🛡️</div>
        <span class="eyebrow" style="text-transform: uppercase; letter-spacing: 0.1em; font-weight: 700; font-size: 0.8rem; color: #e11d48; display: block; margin-bottom: 0.5rem;">403 Forbidden · Security Boundary</span>
        <h1 style="font-size: 1.75rem; font-weight: 700; margin-bottom: 0.75rem;">Access Restricted</h1>
        <p style="color: #64748b; font-size: 0.95rem; line-height: 1.5; margin-bottom: 1.5rem;">
          Your current account role (<strong>{{ auth.userRole() || 'Unknown' }}</strong>) does not have the necessary permissions to access this resource.
        </p>

        <div style="background: #fff1f2; border: 1px solid #fecdd3; border-radius: 8px; padding: 0.85rem; font-size: 0.825rem; color: #9f1239; margin-bottom: 1.75rem; text-align: left;">
          <strong>Security Policy:</strong> Backend authorization is the real security boundary. Access restrictions cannot be bypassed via URL tampering or client state manipulation.
        </div>

        <div style="display: flex; gap: 0.75rem; justify-content: center;">
          <a routerLink="/dashboard" class="primary-button" style="display: inline-block; padding: 0.6rem 1.25rem; text-decoration: none; border-radius: 6px; font-weight: 600;">
            Back to Dashboard
          </a>
          <button type="button" (click)="logout()" style="padding: 0.6rem 1.25rem; border: 1px solid #cbd5e1; background: transparent; border-radius: 6px; font-weight: 600; cursor: pointer;">
            Sign Out
          </button>
        </div>
      </div>
    </main>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class UnauthorizedComponent {
  readonly auth = inject(AuthService);

  logout(): void {
    this.auth.logout();
    location.assign('/login');
  }
}
