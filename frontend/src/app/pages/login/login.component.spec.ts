import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, ActivatedRoute } from '@angular/router';
import { of, throwError } from 'rxjs';
import { LoginComponent } from './login.component';
import { AuthService } from '../../core/auth/auth.service';

describe('LoginComponent', () => {
  let component: LoginComponent;
  let fixture: ComponentFixture<LoginComponent>;
  let authServiceMock: jasmine.SpyObj<AuthService>;
  let routerMock: jasmine.SpyObj<Router>;

  beforeEach(async () => {
    authServiceMock = jasmine.createSpyObj<AuthService>('AuthService', ['login', 'hasRole']);
    routerMock = jasmine.createSpyObj<Router>('Router', ['navigateByUrl']);

    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        { provide: AuthService, useValue: authServiceMock },
        { provide: Router, useValue: routerMock },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: { get: () => null } } }
        }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(LoginComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should initialize with invalid empty form', () => {
    expect(component).toBeTruthy();
    expect(component.form.valid).toBeFalse();
    expect(component.form.controls.username.value).toBe('');
    expect(component.form.controls.password.value).toBe('');
  });

  it('form should be valid when fields are populated', () => {
    component.form.setValue({ username: 'testuser', password: 'Password123!' });
    expect(component.form.valid).toBeTrue();
  });

  it('submit should call auth.login and navigate on success', () => {
    authServiceMock.login.and.returnValue(of({} as any));
    authServiceMock.hasRole.and.returnValue(false);

    component.form.setValue({ username: 'admin', password: 'Password123!' });
    component.submit();

    expect(authServiceMock.login).toHaveBeenCalledWith({ username: 'admin', password: 'Password123!' });
    expect(routerMock.navigateByUrl).toHaveBeenCalledWith('/dashboard');
  });

  it('submit should set errorMessage when login fails with 401', () => {
    authServiceMock.login.and.returnValue(throwError(() => ({ status: 401 })));

    component.form.setValue({ username: 'wrong', password: 'wrong' });
    component.submit();

    expect(component.errorMessage).toBe('Invalid username or password.');
    expect(component.loading).toBeFalse();
  });
});
