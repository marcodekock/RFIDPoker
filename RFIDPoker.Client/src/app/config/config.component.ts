import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { ToastrService } from 'ngx-toastr';
import { CalibrationService } from '../services/calibration.service';
import { RfidDevicesService, RfidDeviceConfig, AntennaFunction } from '../services/rfid-devices.service';
import { AntennaReading, CardMapping, RANK_NAMES, SUIT_NAMES } from '../models';

@Component({
  selector: 'app-config',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="config-container">
      <h1>RFID Hardware</h1>
      <p class="subtitle">Add MUX readers and assign a role to each antenna. Changes take effect immediately &mdash; reader loops restart automatically after saving.</p>

      <section class="devices-section">
        <div class="devices-toolbar">
          <button (click)="addDevice()">+ Add MUX</button>
          <button (click)="reloadDevices()">Reload</button>
          <button class="primary" (click)="saveDevices()" [disabled]="savingDevices">
            {{ savingDevices ? 'Saving…' : 'Save Layout' }}
          </button>
        </div>

        <div *ngIf="devices.length === 0" class="no-devices">
          No MUXes configured. Click <b>+ Add MUX</b> to get started.
        </div>

        <div *ngFor="let device of devices; let di = index" class="device-card">
          <div class="device-header">
            <label>Name
              <input type="text" [(ngModel)]="device.name" placeholder="Pepper1" />
            </label>
            <label class="grow">WebSocket URL
              <input type="text" [(ngModel)]="device.webSocketUrl" placeholder="ws://10.0.0.121/wscomm.cgi" />
            </label>
            <button class="danger" (click)="removeDevice(di)" title="Remove MUX">✕</button>
          </div>

          <table class="antenna-table">
            <thead>
              <tr>
                <th>Port</th>
                <th>Role</th>
                <th>Seat #</th>
                <th>Live</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let ant of device.antennas; let ai = index"
                  [class.active]="antennaTagCount(device, ant) > 0">
                <td>
                  <select [(ngModel)]="ant.antennaIndex">
                    <option *ngFor="let p of availablePorts(device, ant.antennaIndex)" [ngValue]="p">{{ p }}</option>
                  </select>
                </td>
                <td>
                  <select [(ngModel)]="ant.function" (ngModelChange)="onRoleChanged(ant)">
                    <option value="PlayerSeat">Player</option>
                    <option value="Muck">Muck</option>
                    <option value="Flop">Flop</option>
                    <option value="TurnRiver">Turn/River</option>
                  </select>
                </td>
                <td>
                  <input *ngIf="ant.function === 'PlayerSeat'" type="number" min="1" max="9"
                         [(ngModel)]="ant.seatNumber" />
                  <span *ngIf="ant.function !== 'PlayerSeat'" class="dim">—</span>
                </td>
                <td>
                  <span class="live-indicator" [class.on]="antennaTagCount(device, ant) > 0">
                    <span class="dot"></span>
                    <span class="count">{{ antennaTagCount(device, ant) }}</span>
                  </span>
                </td>
                <td>
                  <button class="danger small" (click)="removeAntenna(device, ai)" title="Unassign antenna">✕</button>
                </td>
              </tr>
              <tr *ngIf="device.antennas.length === 0">
                <td colspan="5" class="dim">No antennas assigned.</td>
              </tr>
            </tbody>
          </table>

          <div class="device-footer">
            <button (click)="addAntenna(device)" [disabled]="device.antennas.length >= 8">
              + Add Antenna ({{ device.antennas.length }} / 8)
            </button>
          </div>
        </div>
      </section>

      <h1>Card Calibration</h1>
      <p class="subtitle">Place a single card on an antenna to scan its RFID tag, then assign it to a playing card.</p>

      <section class="scan-section">
        <h2>Live Antenna Readings</h2>
        <button (click)="refreshReadings()">Refresh</button>
        <div class="readings-grid">
          <div *ngFor="let reading of readings" class="reading-card">
            <div class="reading-header">
              <span>{{ reading.deviceName }} - Ant {{ reading.antennaIndex }}</span>
              <span class="func-badge">{{ reading.function }}</span>
            </div>
            <div *ngFor="let tag of reading.tagIds" class="tag-row">
              <code>{{ tag }}</code>
              <button *ngIf="!isMapped(tag)" (click)="startMapping(tag)">Assign</button>
              <span *ngIf="isMapped(tag)" class="mapped">✓ {{ getMappedLabel(tag) }}</span>
            </div>
            <div *ngIf="reading.tagIds.length === 0" class="no-tags">No cards detected</div>
          </div>
        </div>
      </section>

      <section class="assign-section" *ngIf="assigningTag">
        <h2>Assign Tag: <code>{{ assigningTag }}</code></h2>
        <div class="assign-form">
          <label>Rank:
            <select [(ngModel)]="selectedRank">
              <option *ngFor="let r of ranks" [value]="r.value">{{ r.label }}</option>
            </select>
          </label>
          <label>Suit:
            <select [(ngModel)]="selectedSuit">
              <option *ngFor="let s of suits" [value]="s.value">{{ s.label }}</option>
            </select>
          </label>
          <button (click)="saveMapping()">Save</button>
          <button (click)="assigningTag = null">Cancel</button>
        </div>
      </section>

      <section class="mappings-section">
        <h2>Current Mappings ({{ mappings.length }} / 52)</h2>
        <table>
          <thead>
            <tr><th>Tag ID</th><th>Card</th><th></th></tr>
          </thead>
          <tbody>
            <tr *ngFor="let m of mappings">
              <td><code>{{ m.tagId }}</code></td>
              <td>{{ getRankName(m.rank) }} of {{ getSuitName(m.suit) }}</td>
              <td><button (click)="deleteMapping(m.tagId)">✕</button></td>
            </tr>
          </tbody>
        </table>
      </section>
    </div>
  `,
  styles: [`
    .config-container { padding: 1rem; }
    h1 { color: #e94560; margin-bottom: 0.25rem; }
    .subtitle { color: #888; margin-bottom: 1.5rem; }
    section { margin-bottom: 2rem; }
    h2 { font-size: 1.1rem; margin-bottom: 0.75rem; }
    .readings-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 0.75rem; }
    .reading-card { background: #16213e; border: 1px solid #0f3460; border-radius: 6px; padding: 0.75rem; }
    .reading-header { display: flex; justify-content: space-between; margin-bottom: 0.5rem; font-size: 0.85rem; }
    .func-badge { background: #0f3460; padding: 2px 8px; border-radius: 4px; font-size: 0.75rem; }
    .tag-row { display: flex; align-items: center; gap: 0.5rem; margin: 4px 0; }
    .tag-row code { font-size: 0.75rem; background: #0a0a1a; padding: 2px 6px; border-radius: 3px; }
    .no-tags { color: #555; font-size: 0.8rem; }
    .mapped { color: #27ae60; font-size: 0.8rem; }
    .assign-form { display: flex; align-items: center; gap: 1rem; flex-wrap: wrap; }
    .assign-form label { display: flex; align-items: center; gap: 0.5rem; }
    select, button { padding: 0.4rem 0.75rem; border-radius: 4px; border: 1px solid #0f3460; background: #16213e; color: #eee; cursor: pointer; }
    button:hover { background: #0f3460; }
    table { width: 100%; border-collapse: collapse; }
    th, td { text-align: left; padding: 0.5rem; border-bottom: 1px solid #0f3460; }
    th { color: #888; font-size: 0.8rem; }
    td code { font-size: 0.75rem; background: #0a0a1a; padding: 2px 6px; border-radius: 3px; }
    .devices-toolbar { display: flex; gap: 0.5rem; margin-bottom: 1rem; }
    .devices-toolbar .primary { background: #e94560; border-color: #e94560; color: #fff; }
    .devices-toolbar .primary:hover { background: #c8354e; }
    .no-devices { color: #888; padding: 1rem; background: #16213e; border: 1px dashed #0f3460; border-radius: 6px; }
    .device-card { background: #16213e; border: 1px solid #0f3460; border-radius: 6px; padding: 1rem; margin-bottom: 1rem; }
    .device-header { display: flex; gap: 0.75rem; align-items: end; margin-bottom: 0.75rem; flex-wrap: wrap; }
    .device-header label { display: flex; flex-direction: column; gap: 4px; font-size: 0.8rem; color: #aaa; }
    .device-header label.grow { flex: 1 1 300px; }
    .device-header input { padding: 0.4rem 0.6rem; background: #0a0a1a; color: #eee; border: 1px solid #0f3460; border-radius: 4px; min-width: 180px; }
    .antenna-table { width: 100%; border-collapse: collapse; }
    .antenna-table th, .antenna-table td { padding: 0.4rem 0.5rem; border-bottom: 1px solid #0f3460; text-align: left; }
    .antenna-table th { color: #888; font-size: 0.75rem; text-transform: uppercase; }
    .antenna-table input[type=number] { width: 70px; padding: 0.3rem; background: #0a0a1a; color: #eee; border: 1px solid #0f3460; border-radius: 4px; }
    .device-footer { margin-top: 0.75rem; }
    .danger { background: #3a1020; border-color: #6a1a30; color: #f8b0b8; }
    .danger:hover { background: #6a1a30; }
    .danger.small { padding: 0.2rem 0.5rem; font-size: 0.8rem; }
    .dim { color: #555; }
    .antenna-table tr.active td { background: rgba(46, 204, 113, 0.08); }
    .live-indicator { display: inline-flex; align-items: center; gap: 6px; color: #666; font-variant-numeric: tabular-nums; }
    .live-indicator .dot { width: 10px; height: 10px; border-radius: 50%; background: #333; box-shadow: none; transition: background 0.15s, box-shadow 0.15s; }
    .live-indicator.on { color: #7ee8a2; }
    .live-indicator.on .dot { background: #2ecc71; box-shadow: 0 0 6px #2ecc71; animation: pulse 1s ease-in-out infinite; }
    @keyframes pulse { 0%,100% { opacity: 1; } 50% { opacity: 0.55; } }
  `]
})
export class ConfigComponent implements OnInit {
  readings: AntennaReading[] = [];
  mappings: CardMapping[] = [];
  assigningTag: string | null = null;
  selectedRank = 14;
  selectedSuit = 0;

  devices: RfidDeviceConfig[] = [];
  savingDevices = false;

  ranks = Object.entries(RANK_NAMES).map(([v, l]) => ({ value: +v, label: l }));
  suits = Object.entries(SUIT_NAMES).map(([v, l]) => ({ value: +v, label: l }));

  private mappingMap = new Map<string, CardMapping>();
  private readingsSub?: Subscription;

  constructor(
    private calibrationService: CalibrationService,
    private rfidDevices: RfidDevicesService,
    private toastr: ToastrService
  ) {}

  ngOnInit(): void {
    this.loadMappings();
    this.reloadDevices();
    this.readingsSub = this.calibrationService.readings$.subscribe(r => this.readings = r);
    this.calibrationService.refreshReadings();
  }

  ngOnDestroy(): void {
    this.readingsSub?.unsubscribe();
  }

  refreshReadings(): void {
    this.calibrationService.refreshReadings();
  }

  loadMappings(): void {
    this.calibrationService.getMappings().subscribe({
      next: m => {
        this.mappings = m;
        this.mappingMap.clear();
        m.forEach(mapping => this.mappingMap.set(mapping.tagId.toUpperCase(), mapping));
      },
      error: () => {}
    });
  }

  isMapped(tagId: string): boolean {
    return this.mappingMap.has(tagId.toUpperCase());
  }

  getMappedLabel(tagId: string): string {
    const m = this.mappingMap.get(tagId.toUpperCase());
    if (!m) return '';
    return `${RANK_NAMES[m.rank]} of ${SUIT_NAMES[m.suit]}`;
  }

  startMapping(tagId: string): void {
    this.assigningTag = tagId;
  }

  saveMapping(): void {
    if (!this.assigningTag) return;
    this.calibrationService.registerMapping(this.assigningTag, this.selectedRank, this.selectedSuit).subscribe({
      next: () => {
        this.assigningTag = null;
        this.loadMappings();
      }
    });
  }

  deleteMapping(tagId: string): void {
    this.calibrationService.deleteMapping(tagId).subscribe({
      next: () => this.loadMappings()
    });
  }

  getRankName(rank: number): string { return RANK_NAMES[rank] ?? '?'; }
  getSuitName(suit: number): string { return SUIT_NAMES[suit] ?? '?'; }

  // ---- RFID device layout -------------------------------------------------

  reloadDevices(): void {
    this.rfidDevices.getDevices().subscribe({
      next: d => this.devices = (d ?? []).map(x => ({
        name: x.name ?? '',
        webSocketUrl: x.webSocketUrl ?? '',
        antennas: (x.antennas ?? []).map(a => ({ ...a }))
      })),
      error: () => this.toastr.error('Failed to load RFID devices.')
    });
  }

  addDevice(): void {
    this.devices.push({ name: `MUX ${this.devices.length + 1}`, webSocketUrl: '', antennas: [] });
  }

  removeDevice(index: number): void {
    this.devices.splice(index, 1);
  }

  addAntenna(device: RfidDeviceConfig): void {
    if (device.antennas.length >= 8) return;
    const used = new Set(device.antennas.map(a => a.antennaIndex));
    let next = 1;
    while (next <= 8 && used.has(next)) next++;
    device.antennas.push({ antennaIndex: next, function: 'PlayerSeat', seatNumber: 1 });
  }

  removeAntenna(device: RfidDeviceConfig, index: number): void {
    device.antennas.splice(index, 1);
  }

  onRoleChanged(ant: { function: AntennaFunction; seatNumber?: number | null }): void {
    if (ant.function === 'PlayerSeat') {
      if (ant.seatNumber == null) ant.seatNumber = 1;
    } else {
      ant.seatNumber = null;
    }
  }

  availablePorts(device: RfidDeviceConfig, current: number): number[] {
    const used = new Set(device.antennas.map(a => a.antennaIndex).filter(i => i !== current));
    const out: number[] = [];
    for (let i = 1; i <= 8; i++) if (!used.has(i)) out.push(i);
    return out;
  }

  /**
   * Returns the number of tags currently seen on a given antenna, matched by device
   * name + antenna index. Used to show a live pulse dot next to each row so operators
   * can identify which physical antenna they're touching.
   */
  antennaTagCount(device: RfidDeviceConfig, ant: { antennaIndex: number }): number {
    const match = this.readings.find(r =>
      r.deviceName === device.name && r.antennaIndex === ant.antennaIndex);
    return match ? match.tagIds.length : 0;
  }

  saveDevices(): void {
    this.savingDevices = true;
    this.rfidDevices.saveDevices(this.devices).subscribe({
      next: saved => {
        this.savingDevices = false;
        this.devices = saved.map(x => ({ ...x, antennas: x.antennas.map(a => ({ ...a })) }));
        this.toastr.success('RFID layout saved. Reader will reconnect.');
      },
      error: err => {
        this.savingDevices = false;
        const msg = err?.error?.errors?.join('\n') ?? err?.error ?? 'Failed to save RFID layout.';
        this.toastr.error(typeof msg === 'string' ? msg : 'Failed to save RFID layout.');
      }
    });
  }
}
