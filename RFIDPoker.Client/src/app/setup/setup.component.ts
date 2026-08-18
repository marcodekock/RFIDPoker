import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

@Component({
  selector: 'app-setup',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="auth-page">
      <div class="auth-card">
        <h1>First-Time Setup</h1>
        <p class="hint">Create the initial administrator account for this installation.</p>
        <form (ngSubmit)="submit()" #f="ngForm" *ngIf="!blocked()">
          <label>Administrator Username
            <input type="text" name="username" [(ngModel)]="username" required minlength="3" autofocus />
          </label>
          <label>Password
            <input type="password" name="password" [(ngModel)]="password" required minlength="6" />
          </label>
          <label>Confirm Password
            <input type="password" name="confirm" [(ngModel)]="confirm" required />
          </label>
          <div class="error" *ngIf="error()">{{ error() }}</div>
          <button type="submit" class="primary" [disabled]="loading() || !f.valid || password !== confirm">
            {{ loading() ? 'Creating…' : 'Create Administrator' }}
          </button>
        </form>
        <div *ngIf="blocked()" class="error">
          Setup has already been completed. Please sign in instead.
        </div>
      </div>
    </div>
  `,
  styles: [`
    .auth-page { min-height: 100vh; display: flex; align-items: center; justify-content: center; background: #0e1418; color: #e6ecef; font-family: system-ui, sans-serif; }
    .auth-card { background: #172128; border: 1px solid #253340; border-radius: 10px; padding: 32px; min-width: 380px; }
    h1 { margin: 0 0 8px; }
    .hint { color: #a9bcc6; margin-bottom: 18px; font-size: 14px; }
    label { display: block; margin-bottom: 14px; font-size: 13px; color: #a9bcc6; }
    input { display: block; width: 100%; box-sizing: border-box; background: #0f171c; border: 1px solid #2b3a46; color: #e6ecef; padding: 8px 12px; border-radius: 4px; font-size: 14px; margin-top: 4px; }
    button.primary { background: #1f6a3d; border: 1px solid #2d8f52; color: #fff; padding: 10px 18px; border-radius: 4px; cursor: pointer; font-size: 14px; width: 100%; }
    button:disabled { opacity: 0.6; cursor: not-allowed; }
    .error { color: #ff8080; background: #3a1f1f; padding: 8px 10px; border-radius: 4px; font-size: 13px; margin-bottom: 12px; }
  `]
})
export class SetupComponent implements OnInit {
  username = '';
  password = '';
  confirm = '';
  loading = signal(false);
  error = signal('');
  blocked = signal(false);

  constructor(private auth: AuthService, private router: Router) {}

  ngOnInit() {
    this.auth.needsSetup().subscribe({
      next: r => { if (!r.needsSetup) this.blocked.set(true); },
      error: () => this.blocked.set(true)
    });
  }

  submit() {
    if (this.password !== this.confirm) { this.error.set('Passwords do not match.'); return; }
    this.loading.set(true);
    this.error.set('');
    this.auth.setup(this.username, this.password).subscribe({
      next: () => { this.loading.set(false); this.router.navigateByUrl('/manage'); },
      error: e => { this.loading.set(false); this.error.set(e.error?.message || 'Setup failed'); }
    });
  }
}
