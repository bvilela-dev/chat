import { CommonModule } from '@angular/common';
import { Component, inject } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../services/auth.service';

function passwordMatchValidator(control: AbstractControl): ValidationErrors | null {
  const password = control.get('password')?.value;
  const confirmPassword = control.get('confirmPassword')?.value;
  return password && confirmPassword && password !== confirmPassword ? { passwordMismatch: true } : null;
}

@Component({
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  template: `
    <section class="shell">
      <div class="card">
        <p class="eyebrow">Realtime CQRS Chat</p>
        <h1>Create your workspace account</h1>
        <p class="lede">Seu cadastro já gera token JWT, refresh token e entra direto no chat após a criação.</p>

        <form [formGroup]="form" (ngSubmit)="submit()">
          <label>
            <span>Name</span>
            <input type="text" formControlName="name" placeholder="Your full name">
          </label>

          <label>
            <span>Email</span>
            <input type="email" formControlName="email" placeholder="you@team.dev">
          </label>

          <label>
            <span>Password</span>
            <input type="password" formControlName="password" placeholder="Minimum 8 characters">
          </label>

          <label>
            <span>Confirm password</span>
            <input type="password" formControlName="confirmPassword" placeholder="Repeat your password">
          </label>

          <p class="error" *ngIf="form.hasError('passwordMismatch')">Passwords must match.</p>
          <button type="submit" [disabled]="form.invalid || loading">{{ loading ? 'Creating account...' : 'Create account' }}</button>
        </form>

        <p class="error" *ngIf="error">{{ error }}</p>

        <p class="switch-copy">
          Already have an account?
          <a routerLink="/signin">Go to sign in</a>
        </p>
      </div>
    </section>
  `,
  styles: [`
    .shell {
      min-height: 100vh;
      display: grid;
      place-items: center;
      padding: 2rem;
    }

    .card {
      width: min(520px, 100%);
      background: var(--panel);
      border: 1px solid var(--panel-border);
      box-shadow: var(--shadow);
      border-radius: 28px;
      padding: 2rem;
      backdrop-filter: blur(20px);
    }

    .eyebrow {
      font-family: 'Space Grotesk', sans-serif;
      text-transform: uppercase;
      letter-spacing: 0.18em;
      color: var(--accent-strong);
      font-size: 0.75rem;
    }

    h1 {
      font-family: 'Space Grotesk', sans-serif;
      font-size: clamp(2rem, 3vw, 2.8rem);
      margin: 0.3rem 0 0.8rem;
    }

    .lede {
      color: var(--muted);
      margin-bottom: 1.5rem;
    }

    form {
      display: grid;
      gap: 1rem;
    }

    label {
      display: grid;
      gap: 0.4rem;
      color: var(--muted);
    }

    input {
      border: 1px solid rgba(35, 24, 13, 0.15);
      border-radius: 16px;
      padding: 0.9rem 1rem;
      background: rgba(255, 255, 255, 0.8);
    }

    button {
      border: 0;
      border-radius: 16px;
      background: linear-gradient(135deg, var(--accent), var(--accent-strong));
      color: white;
      padding: 1rem;
      cursor: pointer;
      font-weight: 600;
    }

    .error {
      color: #b42318;
      margin: 0;
    }

    .switch-copy {
      margin: 1rem 0 0;
      color: var(--muted);
      text-align: center;
    }

    a {
      color: var(--accent-strong);
      font-weight: 600;
      text-decoration: none;
    }
  `]
})
export class SignUpComponent {
  private readonly formBuilder = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  loading = false;
  error = '';

  readonly form = this.formBuilder.group({
    name: ['', [Validators.required, Validators.maxLength(128)]],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(8)]],
    confirmPassword: ['', [Validators.required]]
  }, { validators: passwordMatchValidator });

  constructor() {
    if (this.authService.isAuthenticated()) {
      void this.router.navigate(['/chat']);
    }
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.loading = true;
    this.error = '';

    const { name, email, password } = this.form.getRawValue();
    this.authService.register(name ?? '', email ?? '', password ?? '').subscribe({
      next: async () => {
        this.loading = false;
        await this.router.navigate(['/chat']);
      },
      error: (errorResponse) => {
        this.loading = false;
        this.error = errorResponse?.error?.title ?? 'Unable to create your account right now.';
      }
    });
  }
}