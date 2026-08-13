import { Injectable, NgZone, OnDestroy } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable } from 'rxjs';
import * as signalR from '@microsoft/signalr';
import { AnalysisResult } from '../models';

@Injectable({ providedIn: 'root' })
export class AnalysisService implements OnDestroy {
  private hubConnection: signalR.HubConnection;
  private analysisSubject = new BehaviorSubject<AnalysisResult | null>(null);
  private stopped = false;

  analysis$ = this.analysisSubject.asObservable();

  constructor(private http: HttpClient, private zone: NgZone) {
    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/analysis')
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: ctx => Math.min(30000, 1000 * Math.pow(2, Math.min(ctx.previousRetryCount, 5)))
      })
      .build();

    this.hubConnection.on('AnalysisUpdated', (result: AnalysisResult) => {
      // SignalR callbacks can fire outside the Angular zone; force change detection.
      this.zone.run(() => this.analysisSubject.next(result));
    });

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
        return;
      } catch (err) {
        console.warn(`SignalR (analysis) connect failed, retrying in ${delay}ms`, err);
        await new Promise(res => setTimeout(res, delay));
        delay = Math.min(delay * 2, 30000);
      }
    }
  }

  getCurrentAnalysis(): Observable<AnalysisResult> {
    return this.http.get<AnalysisResult>('/api/analysis/current');
  }

  ngOnDestroy(): void {
    this.stopped = true;
    this.hubConnection.stop();
  }
}
