import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export type AntennaFunction = 'PlayerSeat' | 'Flop' | 'TurnRiver' | 'Muck';

export interface RfidAntennaConfig {
  antennaIndex: number;
  function: AntennaFunction;
  seatNumber?: number | null;
}

export interface RfidDeviceConfig {
  name: string;
  webSocketUrl: string;
  antennas: RfidAntennaConfig[];
}

@Injectable({ providedIn: 'root' })
export class RfidDevicesService {
  constructor(private http: HttpClient) {}

  getDevices(): Observable<RfidDeviceConfig[]> {
    return this.http.get<RfidDeviceConfig[]>('/api/rfid/devices');
  }

  saveDevices(devices: RfidDeviceConfig[]): Observable<RfidDeviceConfig[]> {
    return this.http.put<RfidDeviceConfig[]>('/api/rfid/devices', devices);
  }
}
