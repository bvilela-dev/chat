import { CommonModule } from '@angular/common';
import { Component, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

@Component({
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  template: `
    <section class="shell">
      <div class="card">
        <p class="eyebrow">Realtime CQRS Chat</p>
        <h1>Sign in to your workspace chat</h1>
        <p class="lede">JWT auth, SignalR commands, and query-side history are already wired behind the gateway.</p>

        <form [formGroup]="form" (ngSubmit)="submit()">
          <label>
            <span>Email</span>
            <input type="email" formControlName="email" placeholder="you@team.dev">
          </label>

          <label>
            <span>Password</span>
            <input type="password" formControlName="password" placeholder="••••••••">
          </label>

          <button type="submit" [disabled]="form.invalid || loading">{{ loading ? 'Signing in...' : 'Enter chat' }}</button>
        </form>

        <p class="error" *ngIf="error">{{ error }}</p>
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
      width: min(460px, 100%);
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
      margin-top: 1rem;
    }
  `]
})
export class LoginComponent {
  private readonly formBuilder = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  loading = false;
  error = '';

  readonly form = this.formBuilder.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]]
  });

  submit(): void {
    if (this.form.invalid) {
      return;
    }

    this.loading = true;
    this.error = '';
    const { email, password } = this.form.getRawValue();
    this.authService.login(email ?? '', password ?? '').subscribe({
      next: async () => {
        this.loading = false;
        await this.router.navigate(['/chat']);
      },
      error: () => {
        this.loading = false;
        this.error = 'Unable to sign in with these credentials.';
      }
    });
  }
}