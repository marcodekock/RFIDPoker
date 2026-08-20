import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

export interface BroadcastStatus { isLive: boolean; }

@Injectable({ providedIn: 'root' })
export class BroadcastService {
  private http = inject(HttpClient);

  // Local snapshot updated by refresh()/start()/stop() so nav pill and controls stay in sync.
  private _isLive = signal(false);
  readonly isLive = this._isLive.asReadonly();
  readonly label = computed(() => this._isLive() ? 'LIVE' : 'OFF-AIR');

  async refresh(): Promise<void> {
    try {
      const s = await firstValueFrom(this.http.get<BroadcastStatus>('/api/broadcast'));
      this._isLive.set(!!s?.isLive);
    } catch {
      // Unauthenticated / offline — leave last known value.
    }
  }

  async start(): Promise<void> {
    const s = await firstValueFrom(this.http.post<BroadcastStatus>('/api/broadcast/start', {}));
    this._isLive.set(!!s?.isLive);
  }

  async stop(): Promise<void> {
    const s = await firstValueFrom(this.http.post<BroadcastStatus>('/api/broadcast/stop', {}));
    this._isLive.set(!!s?.isLive);
  }
}
