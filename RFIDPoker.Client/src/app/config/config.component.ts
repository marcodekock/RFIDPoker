import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { ToastrService } from 'ngx-toastr';
import { CalibrationService } from '../services/calibration.service';
import { RfidDevicesService, RfidDeviceConfig, AntennaFunction } from '../services/rfid-devices.service';
import { DecksService, Deck } from '../services/decks.service';
import { AntennaReading, CardMapping, RANK_NAMES, SUIT_NAMES } from '../models';
import { SpeedScanModalComponent } from './speed-scan-modal.component';

@Component({
  selector: 'app-config',
  standalone: true,
  imports: [CommonModule, FormsModule, SpeedScanModalComponent],
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

      <h1>Card Decks</h1>
      <p class="subtitle">Enable one or more decks; the runtime tag&nbsp;&rarr;&nbsp;card lookup uses the union of every enabled deck.</p>

      <section class="decks-section">
        <div class="decks-toolbar">
          <input type="text" [(ngModel)]="newDeckName" placeholder="New deck name" (keydown.enter)="createDeck()" />
          <button class="primary" (click)="createDeck()" [disabled]="!newDeckName.trim()">+ Add Deck</button>
          <button (click)="reloadDecks()">Reload</button>
        </div>

        <table class="decks-table" *ngIf="decks.length > 0">
          <thead>
            <tr>
              <th>Name</th>
              <th>Mapped Cards</th>
              <th>Enabled</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr *ngFor="let deck of decks" [class.active]="deck.isEnabled">
              <td>
                <input *ngIf="editingDeckId === deck.id" type="text" [(ngModel)]="editingDeckName"
                       (keydown.enter)="commitDeckRename(deck)" (keydown.escape)="cancelDeckRename()" />
                <span *ngIf="editingDeckId !== deck.id">{{ deck.name }}</span>
              </td>
              <td>{{ deck.mappingCount }} / 52</td>
              <td>
                <label class="toggle">
                  <input type="checkbox" [checked]="deck.isEnabled" (change)="toggleDeckEnabled(deck, $event)" />
                </label>
              </td>
              <td class="deck-actions">
                <ng-container *ngIf="editingDeckId !== deck.id">
                  <button (click)="startDeckRename(deck)">Rename</button>
                  <button class="danger small" (click)="deleteDeck(deck)"
                          [disabled]="decks.length <= 1" title="Delete deck">✕</button>
                </ng-container>
                <ng-container *ngIf="editingDeckId === deck.id">
                  <button class="primary" (click)="commitDeckRename(deck)">Save</button>
                  <button (click)="cancelDeckRename()">Cancel</button>
                </ng-container>
              </td>
            </tr>
          </tbody>
        </table>
      </section>

      <h1>Card Calibration</h1>
      <p class="subtitle">Pick a deck to scan into, then place a card on any antenna and assign it. Use <b>Speed Scan</b> to walk the whole deck in order.</p>

      <section class="scan-target">
        <label>Scan target deck:
          <select [(ngModel)]="scanTargetDeckId">
            <option [ngValue]="null" disabled>Select deck&hellip;</option>
            <option *ngFor="let d of decks" [ngValue]="d.id">{{ d.name }} ({{ d.mappingCount }}/52)</option>
          </select>
        </label>
        <button class="primary" (click)="openSpeedScan()">⚡ Speed Scan</button>
      </section>

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
              <button *ngIf="!isMapped(tag)" (click)="startMapping(tag)" [disabled]="scanTargetDeckId == null">Assign</button>
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
        <h2>Current Mappings for {{ scanTargetDeckName() || 'selected deck' }} ({{ filteredMappings().length }} / 52)</h2>
        <table>
          <thead>
            <tr><th>Deck</th><th>Tag ID</th><th>Card</th><th></th></tr>
          </thead>
          <tbody>
            <tr *ngFor="let m of filteredMappings()">
              <td>{{ m.deckName }}</td>
              <td><code>{{ m.tagId }}</code></td>
              <td>{{ getRankName(m.rank) }} of {{ getSuitName(m.suit) }}</td>
              <td><button (click)="deleteMapping(m)">✕</button></td>
            </tr>
          </tbody>
        </table>
      </section>
        <app-speed-scan-modal *ngIf="showSpeedScan"
          [existingDecks]="decks"
          [defaultDeckId]="scanTargetDeckId"
          (close)="onSpeedScanClosed()"
          (completed)="onSpeedScanCompleted()"></app-speed-scan-modal>
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
    .decks-toolbar { display: flex; gap: 0.5rem; margin-bottom: 0.75rem; }
    .decks-toolbar input { padding: 0.4rem 0.6rem; background: #0a0a1a; color: #eee; border: 1px solid #0f3460; border-radius: 4px; min-width: 220px; }
    .decks-toolbar .primary { background: #e94560; border-color: #e94560; color: #fff; }
    .decks-toolbar .primary:hover:not([disabled]) { background: #c8354e; }
    .decks-table { width: 100%; border-collapse: collapse; background: #16213e; border: 1px solid #0f3460; border-radius: 6px; overflow: hidden; }
    .decks-table th, .decks-table td { padding: 0.5rem 0.75rem; border-bottom: 1px solid #0f3460; text-align: left; }
    .decks-table th { color: #888; font-size: 0.75rem; text-transform: uppercase; }
    .decks-table tr.active { background: rgba(233, 69, 96, 0.08); }
    .decks-table input[type=text] { padding: 0.3rem 0.5rem; background: #0a0a1a; color: #eee; border: 1px solid #0f3460; border-radius: 4px; }
    .badge-active { background: #27ae60; color: #fff; border-radius: 4px; padding: 2px 8px; font-size: 0.75rem; font-weight: 600; }
    .toggle input { transform: scale(1.3); cursor: pointer; }
    .scan-target { display: flex; gap: 0.75rem; align-items: end; margin-bottom: 1rem; flex-wrap: wrap; }
    .scan-target label { display: flex; flex-direction: column; gap: 4px; color: #aaa; font-size: 0.8rem; }
    .scan-target select { padding: 0.4rem 0.6rem; background: #0a0a1a; color: #eee; border: 1px solid #0f3460; border-radius: 4px; min-width: 220px; }
    .scan-target .primary { background: #e94560; border-color: #e94560; color: #fff; }
    .scan-target .primary:hover { background: #c8354e; }
    .deck-actions { display: flex; gap: 0.35rem; justify-content: flex-end; }
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

  decks: Deck[] = [];
  newDeckName = '';
  editingDeckId: number | null = null;
  editingDeckName = '';
  scanTargetDeckId: number | null = null;
  showSpeedScan = false;

  ranks = Object.entries(RANK_NAMES).map(([v, l]) => ({ value: +v, label: l }));
  suits = Object.entries(SUIT_NAMES).map(([v, l]) => ({ value: +v, label: l }));

  private mappingMap = new Map<string, CardMapping>();
  private readingsSub?: Subscription;

  constructor(
    private calibrationService: CalibrationService,
    private rfidDevices: RfidDevicesService,
    private decksService: DecksService,
    private toastr: ToastrService
  ) {}

  ngOnInit(): void {
    this.loadMappings();
    this.reloadDevices();
    this.reloadDecks();
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
    if (!this.assigningTag || this.scanTargetDeckId == null) return;
    this.calibrationService.registerMapping(this.scanTargetDeckId, this.assigningTag, this.selectedRank, this.selectedSuit).subscribe({
      next: () => {
        this.assigningTag = null;
        this.loadMappings();
        this.reloadDecks();
      },
      error: err => this.toastr.error(err?.error ?? 'Failed to assign tag.')
    });
  }

  deleteMapping(m: CardMapping): void {
    this.calibrationService.deleteMapping(m.deckId, m.tagId).subscribe({
      next: () => { this.loadMappings(); this.reloadDecks(); }
    });
  }

  openSpeedScan(): void { this.showSpeedScan = true; }

  onSpeedScanClosed(): void {
    this.showSpeedScan = false;
    this.loadMappings();
    this.reloadDecks();
  }

  onSpeedScanCompleted(): void {
    this.toastr.success('Deck complete!');
  }

  toggleDeckEnabled(deck: Deck, ev: Event): void {
    const isEnabled = (ev.target as HTMLInputElement).checked;
    this.decksService.setEnabled(deck.id, isEnabled).subscribe({
      next: () => { deck.isEnabled = isEnabled; this.loadMappings(); },
      error: () => {
        (ev.target as HTMLInputElement).checked = deck.isEnabled;
        this.toastr.error('Failed to update deck.');
      }
    });
  }

  getRankName(rank: number): string { return RANK_NAMES[rank] ?? '?'; }
  getSuitName(suit: number): string { return SUIT_NAMES[suit] ?? '?'; }

  filteredMappings(): CardMapping[] {
    if (this.scanTargetDeckId == null) return this.mappings;
    return this.mappings.filter(m => m.deckId === this.scanTargetDeckId);
  }

  scanTargetDeckName(): string {
    return this.decks.find(d => d.id === this.scanTargetDeckId)?.name ?? '';
  }

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

  // ---- Card decks --------------------------------------------------------

  reloadDecks(): void {
    this.decksService.list().subscribe({
      next: d => {
        this.decks = d;
        if (this.scanTargetDeckId != null && !d.some(x => x.id === this.scanTargetDeckId)) {
          this.scanTargetDeckId = null;
        }
        if (this.scanTargetDeckId == null) {
          this.scanTargetDeckId = d.find(x => x.isEnabled)?.id ?? d[0]?.id ?? null;
        }
      },
      error: () => this.toastr.error('Failed to load decks.')
    });
  }

  createDeck(): void {
    const name = this.newDeckName.trim();
    if (!name) return;
    this.decksService.create(name).subscribe({
      next: () => {
        this.newDeckName = '';
        this.toastr.success(`Deck "${name}" created.`);
        this.reloadDecks();
      },
      error: err => this.toastr.error(err?.error ?? 'Failed to create deck.')
    });
  }

  startDeckRename(deck: Deck): void {
    this.editingDeckId = deck.id;
    this.editingDeckName = deck.name;
  }

  cancelDeckRename(): void {
    this.editingDeckId = null;
    this.editingDeckName = '';
  }

  commitDeckRename(deck: Deck): void {
    const name = this.editingDeckName.trim();
    if (!name || name === deck.name) { this.cancelDeckRename(); return; }
    this.decksService.rename(deck.id, name).subscribe({
      next: () => {
        this.cancelDeckRename();
        this.toastr.success('Deck renamed.');
        this.reloadDecks();
      },
      error: err => this.toastr.error(err?.error ?? 'Failed to rename deck.')
    });
  }

  deleteDeck(deck: Deck): void {
    if (this.decks.length <= 1) return;
    if (!confirm(`Delete deck "${deck.name}" and all its ${deck.mappingCount} card mapping(s)?`)) return;
    this.decksService.delete(deck.id).subscribe({
      next: () => {
        this.toastr.success(`Deck "${deck.name}" deleted.`);
        this.reloadDecks();
        this.loadMappings();
      },
      error: err => this.toastr.error(err?.error ?? 'Failed to delete deck.')
    });
  }
}
