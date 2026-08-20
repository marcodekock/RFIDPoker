import { Component, OnInit, OnDestroy, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';

type CameraRole = 'Main' | 'Secondary';

interface Camera {
  id: number;
  name: string;
  obsSceneName: string;
  role: CameraRole;
  sortOrder: number;
  enabled: boolean;
}

interface CameraStatus {
  enabled: boolean;
  connected: boolean;
  currentScene: string | null;
  desiredScene: string | null;
  handInProgress: boolean;
  broadcastLive: boolean;
}

interface ObsSettings {
  enabled: boolean;
  webSocketUrl: string;
  password: string;
  reconnectDelayMs: number;
  secondaryRotationSeconds: number;
  switchDebounceMs: number;
}

interface Draft {
  id: number | null;
  name: string;
  obsSceneName: string;
  role: CameraRole;
  sortOrder: number;
  enabled: boolean;
}

@Component({
  selector: 'app-cameras',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="page">
      <h1>Cameras &amp; OBS</h1>

      <section class="card" *ngIf="status() as s">
        <h2>Director Status</h2>
        <div class="status-grid">
          <div><span class="label">Enabled:</span>
            <span [class.ok]="s.enabled" [class.bad]="!s.enabled">{{ s.enabled ? 'Yes' : 'No' }}</span>
          </div>
          <div><span class="label">OBS Connected:</span>
            <span [class.ok]="s.connected" [class.bad]="!s.connected">{{ s.connected ? 'Yes' : 'No' }}</span>
          </div>
          <div><span class="label">Hand in progress:</span> {{ s.handInProgress ? 'Yes' : 'No' }}</div>
          <div><span class="label">Desired scene:</span> {{ s.desiredScene || '—' }}</div>
          <div><span class="label">Current scene:</span> {{ s.currentScene || '—' }}</div>
          <div><span class="label">Broadcast:</span>
            <span [class.ok]="s.broadcastLive" [class.bad]="!s.broadcastLive">{{ s.broadcastLive ? 'LIVE' : 'OFF-AIR' }}</span>
          </div>
        </div>
      </section>

      <section class="card">
        <h2>OBS Connection</h2>
        <form (ngSubmit)="saveObs()" *ngIf="obs() as o" class="obs-form">
          <label><input type="checkbox" [(ngModel)]="o.enabled" name="obsEnabled" /> Enable camera director</label>
          <label>WebSocket URL
            <input type="text" [(ngModel)]="o.webSocketUrl" name="obsUrl" placeholder="ws://localhost:4455" />
          </label>
          <label>Password
            <input type="password" [(ngModel)]="o.password" name="obsPwd" placeholder="(leave blank / '********' to keep existing)" />
          </label>
          <label>Reconnect delay (ms)
            <input type="number" [(ngModel)]="o.reconnectDelayMs" name="obsReconnect" min="500" />
          </label>
          <label>Secondary rotation (s)
            <input type="number" [(ngModel)]="o.secondaryRotationSeconds" name="obsRotate" min="1" />
          </label>
          <label>Switch debounce (ms)
            <input type="number" [(ngModel)]="o.switchDebounceMs" name="obsDebounce" min="0" />
          </label>
          <div class="row">
            <button type="submit" class="primary" [disabled]="savingObs()">
              {{ savingObs() ? 'Saving…' : 'Save OBS Settings' }}
            </button>
            <span class="err" *ngIf="obsError()">{{ obsError() }}</span>
            <span class="ok-msg" *ngIf="obsSaved()">Saved. Reconnecting to OBS…</span>
          </div>
        </form>
      </section>

      <section class="card">
        <h2>Cameras</h2>
        <table *ngIf="cameras().length > 0" class="tbl">
          <thead>
            <tr><th>Name</th><th>OBS Scene</th><th>Role</th><th>Order</th><th>Enabled</th><th></th></tr>
          </thead>
          <tbody>
            <tr *ngFor="let c of cameras()">
              <td>{{ c.name }}</td>
              <td><code>{{ c.obsSceneName }}</code></td>
              <td>{{ c.role }}</td>
              <td>{{ c.sortOrder }}</td>
              <td>{{ c.enabled ? '✓' : '' }}</td>
              <td class="actions">
                <button (click)="edit(c)">Edit</button>
                <button class="danger" (click)="remove(c)">Delete</button>
              </td>
            </tr>
          </tbody>
        </table>
        <p *ngIf="cameras().length === 0" class="hint">No cameras configured yet. Add one below.</p>
      </section>

      <section class="card">
        <h2>{{ draft().id ? 'Edit Camera' : 'Add Camera' }}</h2>
        <div class="form">
          <label>Name
            <input type="text" [(ngModel)]="draft().name" placeholder="Overhead" />
          </label>
          <label>OBS Scene Name
            <input type="text" [(ngModel)]="draft().obsSceneName" placeholder="Poker - Overhead" />
          </label>
          <label>Role
            <select [(ngModel)]="draft().role">
              <option value="Main">Main (shown during live hand)</option>
              <option value="Secondary">Secondary (rotates between hands)</option>
            </select>
          </label>
          <label>Sort Order
            <input type="number" [(ngModel)]="draft().sortOrder" />
          </label>
          <label class="check">
            <input type="checkbox" [(ngModel)]="draft().enabled" /> Enabled
          </label>
        </div>
        <div class="row">
          <button class="primary" (click)="save()" [disabled]="saving()">
            {{ saving() ? 'Saving…' : (draft().id ? 'Update' : 'Add') }}
          </button>
          <button *ngIf="draft().id" (click)="reset()">Cancel</button>
          <span class="err" *ngIf="error()">{{ error() }}</span>
        </div>
      </section>
    </div>
  `,
  styles: [`
    .page { min-height: 100vh; background: #0e1418; color: #e6ecef; padding: 24px; font-family: system-ui, sans-serif; }
    .card { background: #172128; border: 1px solid #253340; border-radius: 10px; padding: 18px; margin-bottom: 20px; }
    .card h2 { margin: 0 0 12px; font-size: 16px; }
    .status-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 8px 24px; font-size: 14px; }
    .label { color: #8fa4b0; margin-right: 6px; }
    .ok { color: #6bdb8a; } .bad { color: #ff8080; }
    .hint { color: #8fa4b0; font-size: 13px; margin-top: 10px; }
    code { background: #0f171c; padding: 1px 5px; border-radius: 3px; }
    .tbl { width: 100%; border-collapse: collapse; font-size: 14px; }
    .tbl th, .tbl td { text-align: left; padding: 8px 10px; border-bottom: 1px solid #22303a; }
    .tbl th { color: #8fa4b0; font-weight: 600; }
    .actions button { margin-right: 6px; }
    .form { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 10px 20px; margin-bottom: 12px; }
    .form label { display: flex; flex-direction: column; font-size: 12px; color: #a9bcc6; gap: 4px; }
    .form label.check { flex-direction: row; align-items: center; gap: 6px; font-size: 14px; color: #e6ecef; }
    input[type=text], input[type=number], select {
      background: #0f171c; border: 1px solid #2b3a46; color: #e6ecef; padding: 7px 10px; border-radius: 4px; font-size: 14px;
    }
    button { background: #223440; border: 1px solid #33505f; color: #dbe7ee; padding: 7px 14px; border-radius: 4px; cursor: pointer; font-size: 13px; }
    button.primary { background: #1f6a3d; border-color: #2d8f52; }
    button.danger { background: #6a1f1f; border-color: #8f2d2d; }
    button:disabled { opacity: 0.5; cursor: not-allowed; }
    .row { display: flex; gap: 10px; align-items: center; }
    .err { color: #ff8080; font-size: 13px; }
    .ok-msg { color: #7dffb0; font-size: 13px; }
    .obs-form { display: grid; grid-template-columns: repeat(2, minmax(200px, 1fr)); gap: 10px 16px; }
    .obs-form label { display: flex; flex-direction: column; font-size: 13px; color: #9fb0bb; gap: 4px; }
    .obs-form input[type=text], .obs-form input[type=password], .obs-form input[type=number] {
      background: #0e1418; color: #e6ecef; border: 1px solid #1c2731; border-radius: 4px; padding: 6px 8px;
    }
    .obs-form .row { grid-column: 1 / -1; display: flex; align-items: center; gap: 12px; }
  `]
})
export class CamerasComponent implements OnInit, OnDestroy {
  cameras = signal<Camera[]>([]);
  status = signal<CameraStatus | null>(null);
  draft = signal<Draft>(this.emptyDraft());
  saving = signal(false);
  error = signal<string | null>(null);

  obs = signal<ObsSettings | null>(null);
  savingObs = signal(false);
  obsError = signal<string | null>(null);
  obsSaved = signal(false);

  private statusTimer: any;

  constructor(private http: HttpClient) {}

  ngOnInit(): void {
    this.reload();
    this.refreshStatus();
    this.loadObs();
    this.statusTimer = setInterval(() => this.refreshStatus(), 3000);
  }

  ngOnDestroy(): void {
    if (this.statusTimer) clearInterval(this.statusTimer);
  }

  private reload() {
    this.http.get<Camera[]>('/api/cameras').subscribe({
      next: cs => this.cameras.set(cs),
      error: err => this.error.set(err?.error ?? 'Failed to load cameras.')
    });
  }

  private refreshStatus() {
    this.http.get<CameraStatus>('/api/cameras/status').subscribe({
      next: s => this.status.set(s),
      error: () => { /* silent — surface via connected badge */ }
    });
  }

  edit(c: Camera) {
    this.draft.set({
      id: c.id,
      name: c.name,
      obsSceneName: c.obsSceneName,
      role: c.role,
      sortOrder: c.sortOrder,
      enabled: c.enabled
    });
    this.error.set(null);
  }

  reset() {
    this.draft.set(this.emptyDraft());
    this.error.set(null);
  }

  save() {
    const d = this.draft();
    if (!d.name.trim() || !d.obsSceneName.trim()) {
      this.error.set('Name and OBS scene name are required.');
      return;
    }
    this.saving.set(true);
    this.error.set(null);

    const body = {
      name: d.name,
      obsSceneName: d.obsSceneName,
      role: d.role,
      sortOrder: d.sortOrder,
      enabled: d.enabled
    };

    const req = d.id
      ? this.http.put(`/api/cameras/${d.id}`, body)
      : this.http.post('/api/cameras', body);

    req.subscribe({
      next: () => { this.saving.set(false); this.reset(); this.reload(); },
      error: err => { this.saving.set(false); this.error.set(err?.error ?? 'Save failed.'); }
    });
  }

  remove(c: Camera) {
    if (!confirm(`Delete camera "${c.name}"?`)) return;
    this.http.delete(`/api/cameras/${c.id}`).subscribe({
      next: () => this.reload(),
      error: err => this.error.set(err?.error ?? 'Delete failed.')
    });
  }

  private emptyDraft(): Draft {
    return { id: null, name: '', obsSceneName: '', role: 'Secondary', sortOrder: 0, enabled: true };
  }

  private loadObs() {
    this.http.get<ObsSettings>('/api/obs').subscribe({
      next: s => this.obs.set(s),
      error: err => this.obsError.set(err?.error ?? 'Failed to load OBS settings.')
    });
  }

  saveObs() {
    const s = this.obs();
    if (!s) return;
    this.savingObs.set(true);
    this.obsError.set(null);
    this.obsSaved.set(false);
    this.http.put<ObsSettings>('/api/obs', s).subscribe({
      next: updated => {
        this.savingObs.set(false);
        this.obs.set(updated);
        this.obsSaved.set(true);
        setTimeout(() => this.obsSaved.set(false), 3000);
      },
      error: err => {
        this.savingObs.set(false);
        this.obsError.set(err?.error ?? 'Save failed.');
      }
    });
  }
}
