import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { AntennaReading, CardMapping } from '../models';

@Injectable({ providedIn: 'root' })
export class CalibrationService {
  constructor(private http: HttpClient) {}

  getMappings(): Observable<CardMapping[]> {
    return this.http.get<CardMapping[]>('/api/calibration/mappings');
  }

  registerMapping(tagId: string, rank: number, suit: number): Observable<void> {
    return this.http.post<void>('/api/calibration/mappings', { tagId, rank, suit });
  }

  deleteMapping(tagId: string): Observable<void> {
    return this.http.delete<void>(`/api/calibration/mappings/${tagId}`);
  }

  getAntennaReadings(): Observable<AntennaReading[]> {
    return this.http.get<AntennaReading[]>('/api/calibration/readings');
  }
}
