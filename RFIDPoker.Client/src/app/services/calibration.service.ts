import { Injectable, NgZone, OnDestroy } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable } from 'rxjs';
import * as signalR from '@microsoft/signalr';
import { AntennaReading, CardMapping } from '../models';
import { AuthService } from './auth.service';

@Injectable({ providedIn: 'root' })
export class CalibrationService implements OnDestroy {
  private hubConnection: signalR.HubConnection;
  private readingsSubject = new BehaviorSubject<AntennaReading[]>([]);
  private stopped = false;

  /// Live per-antenna readings pushed from the API over SignalR.
  readings$ = this.readingsSubject.asObservable();

  constructor(private http: HttpClient, private zone: NgZone, private auth: AuthService) {
    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/analysis', { accessTokenFactory: () => this.resolveToken() })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: ctx => Math.min(30000, 1000 * Math.pow(2, Math.min(ctx.previousRetryCount, 5)))
      })
      .build();

    this.hubConnection.on('ReadingsUpdated', (readings: AntennaReading[]) => {
      // SignalR callbacks can fire outside the Angular zone; force change detection.
      this.zone.run(() => this.readingsSubject.next(readings));
    });

    this.hubConnection.onreconnected(() => this.refreshReadings());
    this.hubConnection.onclose(() => {
      if (!this.stopped) this.startWithRetry();
    });

    this.startWithRetry();
  }

  private async startWithRetry(): Promise<void> {
    let delay = 1000;
    while (!this.stopped) {
      try {
        await this.hubConnection.start();
        this.refreshReadings();
        return;
      } catch (err) {
        console.warn(`SignalR (calibration) connect failed, retrying in ${delay}ms`, err);
        await new Promise(res => setTimeout(res, delay));
        delay = Math.min(delay * 2, 30000);
      }
    }
  }

  /// Force a one-shot fetch of the current snapshot (used on startup / reconnect).
  refreshReadings(): void {
    this.http.get<AntennaReading[]>('/api/calibration/readings').subscribe({
      next: r => this.readingsSubject.next(r),
      error: () => {}
    });
  }

  getMappings(): Observable<CardMapping[]> {
    return this.http.get<CardMapping[]>('/api/calibration/mappings');
  }

  registerMapping(deckId: number, tagId: string, rank: number, suit: number): Observable<void> {
    return this.http.post<void>('/api/calibration/mappings', { deckId, tagId, rank, suit });
  }

  deleteMapping(deckId: number, tagId: string): Observable<void> {
    return this.http.request<void>('delete', '/api/calibration/mappings', { body: { deckId, tagId } });
  }

  private resolveToken(): string {
    const user = this.auth.token();
    if (user) return user;
    try {
      const qp = new URLSearchParams(window.location.search);
      return qp.get('token') ?? qp.get('access_token') ?? '';
    } catch { return ''; }
  }

  ngOnDestroy(): void {
    this.stopped = true;
    this.hubConnection.stop();
  }
}
