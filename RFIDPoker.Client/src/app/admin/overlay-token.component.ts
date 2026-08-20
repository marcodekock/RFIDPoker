import { Component, OnInit, OnDestroy, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HttpClient } from '@angular/common/http';

interface OverlayStatus {
  hasActiveToken: boolean;
  createdAt: string | null;
  expiresAt: string | null;
  isRevoked: boolean;
  isExpired: boolean;
}

interface TdAdminStatus {
  enabled: boolean;
  hasActiveToken: boolean;
  tokenCreatedAt: string | null;
  lastUpdatedUtc: string | null;
  latest: TdLatest | null;
}

interface TdLatest {
  level: number;
  breakNum: number;
  isRound: number;
  isBreak: number;
  nextIsBreak: number;
  levelDuration: number;
  nextLevelDuration: number;
  buyins: number;
  playersLeft: number;
  secondsLeft: number;
  clockPaused: number;
  clockPausedSeconds: number;
  smallBlind: number;
  bigBlind: number;
  nextSmallBlind: number;
  nextBigBlind: number;
  chipCount: number;
}

@Component({
  selector: 'app-overlay-token',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <div class="page">
      <h1>Authentication</h1>

      <section class="card">
        <h2>Overlay Token</h2>
        <p class="hint">Token used by the OBS browser source to authenticate the overlay feed.</p>

        <div class="status" *ngIf="status() as s">
          <div><span class="label">State:</span>
            <span [class.ok]="s.hasActiveToken" [class.bad]="!s.hasActiveToken">
              {{ s.hasActiveToken ? 'Active' : (s.isRevoked ? 'Revoked' : (s.isExpired ? 'Expired' : 'None')) }}
            </span>
          </div>
          <div *ngIf="s.createdAt"><span class="label">Created:</span> {{ s.createdAt | date:'medium' }}</div>
          <div *ngIf="s.expiresAt"><span class="label">Expires:</span> {{ s.expiresAt | date:'medium' }}</div>
        </div>

        <div class="row" style="margin-top: 12px;">
          <label>Lifetime (hours):
            <input type="number" min="1" max="720" [(ngModel)]="lifetimeHours" />
          </label>
        </div>

        <div class="row" style="margin-top: 12px;">
          <button class="primary" (click)="generate()" [disabled]="loading()">
            {{ loading() ? 'Generating…' : 'Generate New Token' }}
          </button>
          <button class="danger" (click)="revoke()" [disabled]="!status()?.hasActiveToken">Revoke</button>
        </div>

        <div *ngIf="rawToken()" class="token-box">
          <div class="warn">Save these values now — they will not be shown again.</div>
          <label>OBS Browser Source URL</label>
          <div class="copy-row">
            <input readonly [value]="overlayUrl()" #urlInput />
            <button (click)="copy(urlInput.value)">Copy URL</button>
          </div>
          <label>Raw Token</label>
          <div class="copy-row">
            <input readonly [value]="rawToken()!" #tokenInput />
            <button (click)="copy(tokenInput.value)">Copy Token</button>
          </div>
          <div class="hint">Expires: {{ rawExpires() | date:'medium' }}</div>
        </div>
      </section>

      <section class="card">
        <h2>Tournament Director Token</h2>
        <p class="hint">
          Token used by Tournament Director to POST tournament updates. Enable the integration on the Manage page
          before generating a token. The token is embedded in the URL — no headers required.
        </p>

        <div class="status" *ngIf="tdStatus() as t">
          <div><span class="label">State:</span>
            <span [class.ok]="t.hasActiveToken" [class.bad]="!t.hasActiveToken">{{ t.hasActiveToken ? 'Active' : 'None' }}</span>
          </div>
          <div><span class="label">Integration:</span>
            <span [class.ok]="t.enabled" [class.bad]="!t.enabled">{{ t.enabled ? 'Enabled' : 'Disabled' }}</span>
          </div>
          <div *ngIf="t.tokenCreatedAt"><span class="label">Created:</span> {{ t.tokenCreatedAt | date:'medium' }}</div>
          <div *ngIf="t.lastUpdatedUtc"><span class="label">Last webhook:</span> {{ t.lastUpdatedUtc | date:'medium' }}</div>
          <div *ngIf="t.latest">
            <span class="label">Latest:</span>
            Level {{ t.latest.level }} • {{ t.latest.playersLeft }} players •
            {{ t.latest.smallBlind }}/{{ t.latest.bigBlind }} •
            {{ t.latest.isBreak ? 'ON BREAK' : 'Round' }}
          </div>
        </div>

        <div class="row" style="margin-top: 12px;">
          <button class="primary" (click)="generateTd()" [disabled]="tdLoading()">
            {{ tdLoading() ? 'Generating…' : 'Generate New Token' }}
          </button>
          <button class="danger" (click)="revokeTd()" [disabled]="!tdStatus()?.hasActiveToken">Revoke</button>
        </div>

        <div *ngIf="tdRawToken()" class="token-box">
          <div class="warn">Save this URL now — it will not be shown again.</div>
          <label>Full Webhook URL (token embedded)</label>
          <div class="copy-row">
            <input readonly [value]="tdWebhookUrlWithToken()" #tdFull />
            <button (click)="copy(tdFull.value)">Copy URL</button>
          </div>
          <div class="hint">POST JSON to this URL. No headers required.</div>
        </div>
      </section>
    </div>
  `,
  styles: [`
    .page { min-height: 100vh; background: #0e1418; color: #e6ecef; padding: 24px; font-family: system-ui, sans-serif; }
    header { display: flex; justify-content: space-between; align-items: baseline; margin-bottom: 20px; }
    header nav a { color: #7fb0c8; margin-left: 16px; text-decoration: none; font-size: 14px; }
    .card { background: #172128; border: 1px solid #253340; border-radius: 10px; padding: 18px; margin-bottom: 20px; }
    .card h2 { margin: 0 0 12px; font-size: 16px; }
    .status div { margin-bottom: 6px; font-size: 14px; }
    .label { color: #8fa4b0; margin-right: 8px; }
    .ok { color: #6bdb8a; }
    .bad { color: #ff8080; }
    .row { display: flex; gap: 10px; align-items: center; flex-wrap: wrap; }
    .row label { font-size: 13px; color: #a9bcc6; display: inline-flex; align-items: center; gap: 6px; }
    input { background: #0f171c; border: 1px solid #2b3a46; color: #e6ecef; padding: 7px 10px; border-radius: 4px; font-size: 14px; }
    button { background: #223440; border: 1px solid #33505f; color: #dbe7ee; padding: 7px 14px; border-radius: 4px; cursor: pointer; font-size: 13px; }
    button.primary { background: #1f6a3d; border-color: #2d8f52; }
    button.danger { background: #6a1f26; border-color: #a03039; }
    .token-box { margin-top: 18px; padding: 14px; background: #0f2418; border: 1px solid #2a6b46; border-radius: 8px; }
    .token-box label { display: block; font-size: 12px; color: #8fa4b0; margin: 8px 0 4px; text-transform: uppercase; }
    .copy-row { display: flex; gap: 6px; }
    .copy-row input { flex: 1; font-family: monospace; font-size: 12px; }
    .warn { color: #f0c060; margin-bottom: 10px; font-size: 13px; }
    .hint { color: #8fa4b0; font-size: 12px; margin-top: 8px; }
  `]
})
export class OverlayTokenComponent implements OnInit, OnDestroy {
  status = signal<OverlayStatus | null>(null);
  lifetimeHours = 24;
  loading = signal(false);
  rawToken = signal<string | null>(null);
  rawExpires = signal<string | null>(null);

  tdStatus = signal<TdAdminStatus | null>(null);
  tdLoading = signal(false);
  tdRawToken = signal<string | null>(null);
  private tdTimer: any;

  constructor(private http: HttpClient) {}

  ngOnInit() {
    this.reload();
    this.reloadTd();
    // Refresh TD status every 5s so operators see incoming webhooks live.
    this.tdTimer = setInterval(() => this.reloadTd(), 5000);
  }

  ngOnDestroy() {
    if (this.tdTimer) clearInterval(this.tdTimer);
  }

  reload() {
    this.http.get<OverlayStatus>('/api/overlay-token/status').subscribe(r => this.status.set(r));
  }

  generate() {
    this.loading.set(true);
    this.http.post<{ token: string; expiresAt: string }>('/api/overlay-token/generate', { lifetimeHours: this.lifetimeHours })
      .subscribe({
        next: r => {
          this.loading.set(false);
          this.rawToken.set(r.token);
          this.rawExpires.set(r.expiresAt);
          this.reload();
        },
        error: e => { this.loading.set(false); alert(e.error?.message || 'Failed'); }
      });
  }

  revoke() {
    if (!confirm('Revoke the active overlay token? OBS will lose access immediately.')) return;
    this.http.post('/api/overlay-token/revoke', {}).subscribe({
      next: () => { this.rawToken.set(null); this.reload(); },
      error: e => alert(e.error?.message || 'Failed')
    });
  }

  overlayUrl(): string {
    const t = this.rawToken();
    if (!t) return '';
    return `${location.origin}/overlay?token=${encodeURIComponent(t)}`;
  }

  copy(v: string) {
    navigator.clipboard.writeText(v).then(() => {/* noop */});
  }

  // --- Tournament Director ---------------------------------------------------
  reloadTd() {
    this.http.get<TdAdminStatus>('/api/tournament-director/status').subscribe({
      next: r => this.tdStatus.set(r),
      error: () => { /* silent */ }
    });
  }

  toggleTdEnabled(e: Event) {
    const enabled = (e.target as HTMLInputElement).checked;
    this.http.put<TdAdminStatus>('/api/tournament-director/settings', { enabled }).subscribe({
      next: () => this.reloadTd(),
      error: err => alert(err.error?.message || 'Failed to update TD settings')
    });
  }

  generateTd() {
    this.tdLoading.set(true);
    this.http.post<{ token: string }>('/api/tournament-director/generate', {}).subscribe({
      next: r => {
        this.tdLoading.set(false);
        this.tdRawToken.set(r.token);
        this.reloadTd();
      },
      error: e => { this.tdLoading.set(false); alert(e.error?.message || 'Failed'); }
    });
  }

  revokeTd() {
    if (!confirm('Revoke the active TD token? Tournament Director will lose access immediately.')) return;
    this.http.post('/api/tournament-director/revoke', {}).subscribe({
      next: () => { this.tdRawToken.set(null); this.reloadTd(); },
      error: e => alert(e.error?.message || 'Failed')
    });
  }

  webhookUrl(): string {
    return `${location.origin}/api/tournament-director/webhook`;
  }

  tdWebhookUrlWithToken(): string {
    const t = this.tdRawToken();
    if (!t) return '';
    return `${this.webhookUrl()}?token=${encodeURIComponent(t)}`;
  }
}
