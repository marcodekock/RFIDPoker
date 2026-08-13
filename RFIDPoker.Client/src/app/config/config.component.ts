import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { CalibrationService } from '../services/calibration.service';
import { AntennaReading, CardMapping, RANK_NAMES, SUIT_NAMES } from '../models';

@Component({
  selector: 'app-config',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="config-container">
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
  `]
})
export class ConfigComponent implements OnInit {
  readings: AntennaReading[] = [];
  mappings: CardMapping[] = [];
  assigningTag: string | null = null;
  selectedRank = 14;
  selectedSuit = 0;

  ranks = Object.entries(RANK_NAMES).map(([v, l]) => ({ value: +v, label: l }));
  suits = Object.entries(SUIT_NAMES).map(([v, l]) => ({ value: +v, label: l }));

  private mappingMap = new Map<string, CardMapping>();
  private readingsSub?: Subscription;

  constructor(private calibrationService: CalibrationService) {}

  ngOnInit(): void {
    this.loadMappings();
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
}
