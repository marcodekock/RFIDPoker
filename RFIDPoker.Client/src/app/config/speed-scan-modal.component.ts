import { Component, EventEmitter, Input, OnDestroy, OnInit, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { ToastrService } from 'ngx-toastr';
import { CalibrationService } from '../services/calibration.service';
import { DecksService, Deck } from '../services/decks.service';
import { AntennaReading, RANK_NAMES, SUIT_NAMES, SUIT_SYMBOLS } from '../models';

interface CardStep { rank: number; suit: number; }

@Component({
  selector: 'app-speed-scan-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="modal-backdrop" (click)="close.emit()">
      <div class="modal" (click)="$event.stopPropagation()">
        <div class="modal-header">
          <h2>Speed Scan</h2>
          <button class="close-x" (click)="close.emit()">&times;</button>
        </div>

        <!-- Setup phase -->
        <div *ngIf="phase === 'setup'" class="setup">
          <p class="hint">Choose an existing deck or create a new one. Once started, tap each card on any antenna in turn &mdash; it will be assigned automatically.</p>

          <div class="setup-row">
            <label><input type="radio" name="mode" value="existing" [(ngModel)]="mode" /> Use existing deck</label>
            <select [(ngModel)]="selectedDeckId" [disabled]="mode !== 'existing'">
              <option [ngValue]="null" disabled>Select deck&hellip;</option>
              <option *ngFor="let d of existingDecks" [ngValue]="d.id">{{ d.name }} ({{ d.mappingCount }}/52)</option>
            </select>
          </div>

          <div class="setup-row">
            <label><input type="radio" name="mode" value="new" [(ngModel)]="mode" /> Create new deck</label>
            <input type="text" placeholder="New deck name" [(ngModel)]="newDeckName" [disabled]="mode !== 'new'" />
          </div>

          <div class="setup-actions">
            <button (click)="close.emit()">Cancel</button>
            <button class="primary" [disabled]="!canStart() || starting" (click)="start()">
              {{ starting ? 'Starting&hellip;' : 'Start Speed Scan' }}
            </button>
          </div>
        </div>

        <!-- Scan phase -->
        <div *ngIf="phase === 'scan'" class="scan">
          <div class="progress">
            <div class="progress-text">
              Card <b>{{ currentIndex + 1 }}</b> of {{ order.length }}
              &mdash; <span class="deck-name">{{ deckName }}</span>
            </div>
            <div class="progress-bar"><div class="progress-fill" [style.width.%]="progressPct()"></div></div>
          </div>

          <div class="speed-card-display" [class.red]="isRed(currentCard().suit)">
            <div class="card-rank">{{ rankLabel(currentCard().rank) }}</div>
            <div class="card-suit">{{ suitSymbol(currentCard().suit) }}</div>
            <div class="card-name">{{ rankLabel(currentCard().rank) }} of {{ suitName(currentCard().suit) }}</div>
          </div>

          <div class="status">
            <span *ngIf="waitingForTag" class="pulse">Waiting for tag&hellip;</span>
            <span *ngIf="assigning" class="pulse">Assigning &hellip;</span>
            <span *ngIf="lastMessage" class="last">{{ lastMessage }}</span>
          </div>

          <div class="scan-actions">
            <button (click)="skip()">Skip</button>
            <button (click)="close.emit()">Finish</button>
          </div>
        </div>

        <!-- Done phase -->
        <div *ngIf="phase === 'done'" class="done">
          <h3>Deck Complete &#127881;</h3>
          <p>All 52 cards assigned to <b>{{ deckName }}</b>.</p>
          <button class="primary" (click)="close.emit()">Close</button>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .modal-backdrop {
      position: fixed; inset: 0; background: rgba(0,0,0,0.75);
      display: flex; align-items: center; justify-content: center; z-index: 1000;
    }
    .modal {
      background: #16213e; border: 1px solid #0f3460; border-radius: 8px;
      width: min(560px, 92vw); max-height: 92vh; overflow: auto;
      color: #eee; box-shadow: 0 20px 60px rgba(0,0,0,0.6);
    }
    .modal-header { display: flex; align-items: center; justify-content: space-between; padding: 1rem 1.25rem; border-bottom: 1px solid #0f3460; }
    .modal-header h2 { margin: 0; color: #e94560; }
    .close-x { background: transparent; border: 0; color: #aaa; font-size: 1.5rem; cursor: pointer; }
    .setup, .scan, .done { padding: 1.25rem; }
    .hint { color: #aaa; margin-top: 0; }
    .setup-row { display: flex; align-items: center; gap: 0.75rem; margin: 0.6rem 0; flex-wrap: wrap; }
    .setup-row label { display: flex; align-items: center; gap: 0.4rem; min-width: 200px; }
    .setup-row select, .setup-row input[type=text] {
      flex: 1 1 200px; padding: 0.45rem 0.6rem; background: #0a0a1a; color: #eee;
      border: 1px solid #0f3460; border-radius: 4px;
    }
    .setup-actions, .scan-actions { display: flex; justify-content: flex-end; gap: 0.5rem; margin-top: 1rem; }
    button { padding: 0.5rem 0.9rem; border-radius: 4px; border: 1px solid #0f3460; background: #16213e; color: #eee; cursor: pointer; }
    button:hover:not([disabled]) { background: #0f3460; }
    .primary { background: #e94560; border-color: #e94560; color: #fff; }
    .primary:hover:not([disabled]) { background: #c8354e; }
    button[disabled] { opacity: 0.5; cursor: not-allowed; }

    .progress { margin-bottom: 1rem; }
    .progress-text { display: flex; justify-content: space-between; margin-bottom: 4px; color: #aaa; font-size: 0.9rem; }
    .deck-name { color: #e94560; font-weight: 600; }
    .progress-bar { height: 8px; background: #0a0a1a; border: 1px solid #0f3460; border-radius: 4px; overflow: hidden; }
    .progress-fill { height: 100%; background: #27ae60; transition: width 0.2s; }

    .speed-card-display {
      background: #fdfdfd; color: #111; border-radius: 12px;
      padding: 1.25rem 1rem; text-align: center;
      box-shadow: inset 0 0 0 3px #ddd, 0 6px 20px rgba(0,0,0,0.5);
      user-select: none;
      display: flex; flex-direction: column; align-items: center; justify-content: center;
      overflow: hidden;
      width: 100%; min-height: 220px; max-height: 55vh;
      box-sizing: border-box;
    }
    .speed-card-display.red { color: #c81a2b; }
    .speed-card-display .card-rank { font-size: 3rem; line-height: 1; font-weight: 800; }
    .speed-card-display .card-suit { font-size: 4rem; line-height: 1; margin: 0.25rem 0; }
    .speed-card-display .card-name { font-size: 0.95rem; color: #333; letter-spacing: 0.05em; text-transform: uppercase; margin-top: 0.25rem; }
    .speed-card-display.red .card-name { color: #7a1220; }

    .status { text-align: center; margin: 0.75rem 0 0; min-height: 1.5em; color: #aaa; }
    .pulse { animation: pulseFade 1.4s ease-in-out infinite; }
    .last { color: #7ee8a2; }
    @keyframes pulseFade { 0%,100% { opacity: 1; } 50% { opacity: 0.4; } }

    .done { text-align: center; }
    .done h3 { color: #27ae60; }
  `]
})
export class SpeedScanModalComponent implements OnInit, OnDestroy {
  @Input() existingDecks: Deck[] = [];
  @Input() defaultDeckId: number | null = null;
  @Output() close = new EventEmitter<void>();
  @Output() completed = new EventEmitter<void>();

  phase: 'setup' | 'scan' | 'done' = 'setup';
  mode: 'existing' | 'new' = 'existing';
  selectedDeckId: number | null = null;
  newDeckName = '';
  starting = false;

  deckId: number | null = null;
  deckName = '';
  order: CardStep[] = [];
  currentIndex = 0;
  waitingForTag = true;
  assigning = false;
  lastMessage = '';

  /** Tag IDs already consumed within this modal instance to avoid re-triggering on the same card. */
  private usedTags = new Set<string>();
  private sub?: Subscription;

  constructor(
    private calibration: CalibrationService,
    private decksService: DecksService,
    private toastr: ToastrService
  ) {}

  ngOnInit(): void {
    this.order = SpeedScanModalComponent.buildOrder();
    if (this.defaultDeckId != null) {
      this.selectedDeckId = this.defaultDeckId;
    } else if (this.existingDecks.length > 0) {
      this.selectedDeckId = this.existingDecks[0].id;
    }
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
  }

  private static buildOrder(): CardStep[] {
    // Ace treated as high (14) so Ace-through-King renders as A,2..10,J,Q,K.
    const ranks = [14, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13];
    // Suit enum: Hearts=0, Diamonds=1, Clubs=2, Spades=3.
    // Requested outer order: Spades → Diamonds → Hearts → Clubs.
    const suits = [3, 1, 0, 2];
    const out: CardStep[] = [];
    for (const s of suits) for (const r of ranks) out.push({ rank: r, suit: s });
    return out;
  }

  canStart(): boolean {
    if (this.mode === 'existing') return this.selectedDeckId != null;
    return this.newDeckName.trim().length > 0;
  }

  start(): void {
    if (!this.canStart()) return;
    this.starting = true;
    if (this.mode === 'new') {
      const name = this.newDeckName.trim();
      this.decksService.create(name).subscribe({
        next: d => this.beginScan(d.id, d.name),
        error: err => {
          this.starting = false;
          this.toastr.error(err?.error ?? 'Failed to create deck.');
        }
      });
    } else {
      const d = this.existingDecks.find(x => x.id === this.selectedDeckId);
      this.beginScan(this.selectedDeckId!, d?.name ?? '');
    }
  }

  private beginScan(deckId: number, deckName: string): void {
    this.deckId = deckId;
    this.deckName = deckName;
    this.currentIndex = 0;
    this.usedTags.clear();
    this.phase = 'scan';
    this.waitingForTag = true;
    this.starting = false;

    this.sub = this.calibration.readings$.subscribe(readings => this.onReadings(readings));
    this.calibration.refreshReadings();
  }

  private onReadings(readings: AntennaReading[]): void {
    if (this.phase !== 'scan' || this.assigning) return;

    // Consider any tag currently visible on any antenna as a candidate. First one
    // that hasn't already been consumed this session is assigned to the current card.
    for (const r of readings) {
      for (const tagId of r.tagIds) {
        const key = tagId.toUpperCase();
        if (this.usedTags.has(key)) continue;
        this.assignTag(tagId);
        return;
      }
    }
  }

  private assignTag(tagId: string): void {
    if (this.deckId == null) return;
    const card = this.currentCard();
    this.assigning = true;
    this.waitingForTag = false;

    this.calibration.registerMapping(this.deckId, tagId, card.rank, card.suit).subscribe({
      next: () => {
        this.usedTags.add(tagId.toUpperCase());
        this.lastMessage = `Assigned ${this.rankLabel(card.rank)} of ${this.suitName(card.suit)}`;
        this.assigning = false;
        this.advance();
      },
      error: err => {
        this.assigning = false;
        this.waitingForTag = true;
        this.toastr.error(err?.error ?? 'Failed to register tag.');
      }
    });
  }

  private advance(): void {
    if (this.currentIndex + 1 >= this.order.length) {
      this.phase = 'done';
      this.completed.emit();
      return;
    }
    this.currentIndex++;
    this.waitingForTag = true;
  }

  skip(): void { this.advance(); }

  currentCard(): CardStep { return this.order[this.currentIndex] ?? { rank: 14, suit: 3 }; }
  progressPct(): number { return Math.round(((this.currentIndex) / this.order.length) * 100); }
  rankLabel(r: number): string { return RANK_NAMES[r] ?? '?'; }
  suitName(s: number): string { return SUIT_NAMES[s] ?? '?'; }
  suitSymbol(s: number): string { return SUIT_SYMBOLS[s] ?? '?'; }
  isRed(s: number): boolean { return s === 0 || s === 1; }
}
