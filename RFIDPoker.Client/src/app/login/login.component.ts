import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../services/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <div class="auth-page">
      <div class="auth-card">
        <h1>Sign In</h1>
        <form (ngSubmit)="submit()" #f="ngForm" *ngIf="!needsSetup()">
          <label>Username
            <input type="text" name="username" [(ngModel)]="username" required autofocus />
          </label>
          <label>Password
            <input type="password" name="password" [(ngModel)]="password" required />
          </label>
          <div class="error" *ngIf="error()">{{ error() }}</div>
          <button type="submit" class="primary" [disabled]="loading() || !f.valid">
            {{ loading() ? 'Signing in…' : 'Sign In' }}
          </button>
        </form>
        <div *ngIf="needsSetup()" class="setup-hint">
          <p>No accounts exist yet. Create the first administrator to get started.</p>
          <a routerLink="/setup" class="primary-link">Run first-time setup →</a>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .auth-page { min-height: 100vh; display: flex; align-items: center; justify-content: center; background: #0e1418; color: #e6ecef; font-family: system-ui, sans-serif; }
    .auth-card { background: #172128; border: 1px solid #253340; border-radius: 10px; padding: 32px; min-width: 340px; }
    h1 { margin: 0 0 20px; }
    label { display: block; margin-bottom: 14px; font-size: 13px; color: #a9bcc6; }
    input { display: block; width: 100%; box-sizing: border-box; background: #0f171c; border: 1px solid #2b3a46; color: #e6ecef; padding: 8px 12px; border-radius: 4px; font-size: 14px; margin-top: 4px; }
    button.primary { background: #1f6a3d; border: 1px solid #2d8f52; color: #fff; padding: 10px 18px; border-radius: 4px; cursor: pointer; font-size: 14px; width: 100%; }
    button.primary:hover { background: #278349; }
    button:disabled { opacity: 0.6; cursor: not-allowed; }
    .error { color: #ff8080; background: #3a1f1f; padding: 8px 10px; border-radius: 4px; font-size: 13px; margin-bottom: 12px; }
    .setup-hint { text-align: center; }
    .primary-link { color: #7fb0c8; }
  `]
})
export class LoginComponent implements OnInit {
  username = '';
  password = '';
  loading = signal(false);
  error = signal('');
  needsSetup = signal(false);

  constructor(private auth: AuthService, private router: Router, private route: ActivatedRoute) {}

  ngOnInit() {
    this.auth.needsSetup().subscribe(r => {
      if (r.needsSetup) this.router.navigateByUrl('/setup');
    });
  }

  submit() {
    this.loading.set(true);
    this.error.set('');
    this.auth.login(this.username, this.password).subscribe({
      next: () => {
        this.loading.set(false);
        const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl') || '/display';
        this.router.navigateByUrl(returnUrl);
      },
      error: e => {
        this.loading.set(false);
        this.error.set(e.error?.message || 'Sign in failed');
      }
    });
  }
}
