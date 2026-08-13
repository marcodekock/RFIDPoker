import { Injectable, OnDestroy } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable } from 'rxjs';
import * as signalR from '@microsoft/signalr';
import { AnalysisResult } from '../models';

@Injectable({ providedIn: 'root' })
export class AnalysisService implements OnDestroy {
  private hubConnection: signalR.HubConnection;
  private analysisSubject = new BehaviorSubject<AnalysisResult | null>(null);

  analysis$ = this.analysisSubject.asObservable();

  constructor(private http: HttpClient) {
    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/analysis')
      .withAutomaticReconnect()
      .build();

    this.hubConnection.on('AnalysisUpdated', (result: AnalysisResult) => {
      this.analysisSubject.next(result);
    });

    this.hubConnection.start().catch(err => console.error('SignalR connection error:', err));
  }

  getCurrentAnalysis(): Observable<AnalysisResult> {
    return this.http.get<AnalysisResult>('/api/analysis/current');
  }

  ngOnDestroy(): void {
    this.hubConnection.stop();
  }
}
