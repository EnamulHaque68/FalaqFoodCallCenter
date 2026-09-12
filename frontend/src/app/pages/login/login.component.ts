import { ChangeDetectionStrategy, ChangeDetectorRef, Component, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule],
  template: `
    <main class="login-page">
      <section class="login-brand-panel">
        <div class="brand large"><span class="brand-mark">FF</span><div><strong>Falaq Food</strong><small>Call Center Platform</small></div></div>
        <div class="brand-copy"><span class="eyebrow">Enterprise Operations</span><h1>Connect every customer conversation to the right agent.</h1><p>A secure workspace for voice operations, customer service, routing and real-time call management.</p></div>
        <div class="brand-footer">In-house Call Center Platform · MVP</div>
      </section>
      <section class="login-form-panel">
        <div class="login-card">
          <span class="eyebrow">Secure sign in</span>
          <h2>Welcome back</h2>
          <p class="muted">Sign in with your call center account to continue.</p>
          <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
            <label>Username<input type="text" formControlName="username" autocomplete="username" placeholder="Enter username"></label>
            @if (form.controls.username.touched && form.controls.username.invalid) { <small class="field-error">Username is required.</small> }
            <label>Password
              <div style="position: relative; display: flex; align-items: center;">
                <input [type]="showPassword ? 'text' : 'password'" formControlName="password" autocomplete="current-password" placeholder="Enter password" style="width: 100%; padding-right: 50px;">
                <button type="button" (click)="showPassword = !showPassword" style="position: absolute; right: 12px; background: none; border: none; cursor: pointer; font-size: 12px; font-weight: 600; color: #64748b; padding: 4px;" [title]="showPassword ? 'Hide password' : 'Show password'">
                  {{ showPassword ? 'Hide' : 'Show' }}
                </button>
              </div>
            </label>
            @if (form.controls.password.touched && form.controls.password.invalid) { <small class="field-error">Password is required.</small> }
            @if (errorMessage) { <div class="error-banner">{{ errorMessage }}</div> }
            <button class="primary-button" type="submit" [disabled]="form.invalid || loading">{{ loading ? 'Signing in…' : 'Sign in' }}</button>
          </form>
          <div class="security-note">JWT-secured access · Role-based authorization</div>
        </div>
      </section>
    </main>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class LoginComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly cdr = inject(ChangeDetectorRef);

  readonly form = this.fb.nonNullable.group({ username: ['', Validators.required], password: ['', Validators.required] });
  loading = false;
  errorMessage = '';
  showPassword = false;

  submit(): void {
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    this.loading = true;
    this.errorMessage = '';
    this.auth.login(this.form.getRawValue()).pipe(finalize(() => {
      this.loading = false;
      this.cdr.markForCheck();
    })).subscribe({
      next: () => {
        const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
        if (returnUrl) {
          void this.router.navigateByUrl(returnUrl);
        } else if (this.auth.hasRole('Agent') && !this.auth.hasRole('Admin') && !this.auth.hasRole('Supervisor')) {
          void this.router.navigateByUrl('/agent-dashboard');
        } else {
          void this.router.navigateByUrl('/dashboard');
        }
      },
      error: (error) => {
        this.errorMessage = error?.status === 401 ? 'Invalid username or password.' : 'Unable to sign in. Please check the API connection.';
        this.cdr.markForCheck();
      }
    });
  }
}
