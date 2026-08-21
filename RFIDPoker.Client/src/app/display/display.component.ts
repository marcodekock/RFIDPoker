import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subscription } from 'rxjs';
import { AnalysisService } from '../services/analysis.service';
import { AnalysisResult, PlayerAnalysis, RANK_NAMES, SUIT_SYMBOLS, SUIT_NAMES, STREET_NAMES } from '../models';

@Component({
  selector: 'app-display',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="display-container" *ngIf="analysis">
      <div class="break-overlay" *ngIf="analysis.break?.isActive">
        <div class="break-label">{{ analysis.break?.label || 'BREAK' }}</div>
        <div class="break-time">{{ breakTimeDisplay() }}</div>
        <div class="break-sub" *ngIf="analysis.break?.isPaused">PAUSED</div>
      </div>
      <ng-container *ngIf="!analysis.break?.isActive">
      <div class="muck-panel" *ngIf="analysis.muckedCards?.length">
        <h3>Muck <span class="muck-count">{{ analysis.muckedCards.length }}</span></h3>
        <div class="muck-cards">
          <div *ngFor="let card of analysis.muckedCards; trackBy: trackCard"
               class="card-display small"
               [ngClass]="getSuitClass(card.suit)">
            <span class="rank">{{ getRank(card) }}</span>
            <span class="suit">{{ getSuit(card) }}</span>
          </div>
        </div>
      </div>

      <header>
        <span class="street-badge">{{ getStreetName(analysis.currentStreet) }}</span>
        <span class="blinds-badge" *ngIf="analysis.tournament">
          Level {{ analysis.tournament.level }} • Blinds {{ analysis.tournament.smallBlind | number }}/{{ analysis.tournament.bigBlind | number }}
        </span>
        <span class="blinds-badge" *ngIf="!analysis.tournament && analysis.blinds">Blinds {{ analysis.blinds }}</span>
        <span class="player-count">{{ analysis.activePlayerCount }} players</span>
        <span class="player-count" *ngIf="analysis.tournament">
          Avg {{ analysis.tournament.averageStack | number }}
        </span>
      </header>

      <section class="table-layout">
        <div class="players-column left">
          <div *ngFor="let player of leftPlayers; trackBy: trackPlayer"
               class="player-card"
               [class.folded]="player.folded">
            <div class="player-header">
              <span class="seat">Seat {{ player.seatNumber }}</span>
              <span class="name">{{ player.playerName }}</span>
              <span class="fold-badge" *ngIf="player.folded">FOLDED</span>
            </div>

            <div class="chip-count" [class.placeholder]="player.chipCount == null">
              <span *ngIf="player.chipCount != null">{{ player.chipCount | number }} chips</span>
              <span *ngIf="player.chipCount == null">&nbsp;</span>
            </div>

            <div class="hole-cards" *ngIf="player.holeCards?.length">
              <div *ngFor="let card of player.holeCards; trackBy: trackCard"
                   class="card-display"
                   [ngClass]="getSuitClass(card.suit)"
                   [class.dim]="player.folded">
                <span class="rank">{{ getRank(card) }}</span>
                <span class="suit">{{ getSuit(card) }}</span>
              </div>
            </div>

            <ng-container *ngIf="!player.folded">
              <div class="hand-info" *ngIf="player.handDescription">
                <span class="hand-desc">{{ player.handDescription }}</span>
              </div>

              <div class="equity" *ngIf="player.winPercentage > 0">
                <div class="equity-row">
                  <span>Win</span>
                  <div class="equity-bar"><div class="fill" [style.width.%]="player.winPercentage"></div></div>
                  <span>{{ player.winPercentage | number:'1.1-1' }}%</span>
                </div>
                <div class="equity-row">
                  <span>Tie</span>
                  <div class="equity-bar"><div class="fill tie" [style.width.%]="player.tiePercentage"></div></div>
                  <span>{{ player.tiePercentage | number:'1.1-1' }}%</span>
                </div>
              </div>
            </ng-container>
          </div>
        </div>

        <section class="community-cards">
          <h2>Board</h2>
          <div class="cards-row">
            <div *ngFor="let card of analysis.communityCards; trackBy: trackCard"
                 class="card-display"
                 [ngClass]="getSuitClass(card.suit)">
              <span class="rank">{{ getRank(card) }}</span>
              <span class="suit">{{ getSuit(card) }}</span>
            </div>
            <div *ngFor="let slot of getEmptySlots(); trackBy: trackIndex" class="card-display empty">?</div>
          </div>
        </section>

        <div class="players-column right">
          <div *ngFor="let player of rightPlayers; trackBy: trackPlayer"
               class="player-card"
               [class.folded]="player.folded">
            <div class="player-header">
              <span class="seat">Seat {{ player.seatNumber }}</span>
              <span class="name">{{ player.playerName }}</span>
              <span class="fold-badge" *ngIf="player.folded">FOLDED</span>
            </div>

            <div class="chip-count" [class.placeholder]="player.chipCount == null">
              <span *ngIf="player.chipCount != null">{{ player.chipCount | number }} chips</span>
              <span *ngIf="player.chipCount == null">&nbsp;</span>
            </div>

            <div class="hole-cards" *ngIf="player.holeCards?.length">
              <div *ngFor="let card of player.holeCards; trackBy: trackCard"
                   class="card-display"
                   [ngClass]="getSuitClass(card.suit)"
                   [class.dim]="player.folded">
                <span class="rank">{{ getRank(card) }}</span>
                <span class="suit">{{ getSuit(card) }}</span>
              </div>
            </div>

            <ng-container *ngIf="!player.folded">
              <div class="hand-info" *ngIf="player.handDescription">
                <span class="hand-desc">{{ player.handDescription }}</span>
              </div>

              <div class="equity" *ngIf="player.winPercentage > 0">
                <div class="equity-row">
                  <span>Win</span>
                  <div class="equity-bar"><div class="fill" [style.width.%]="player.winPercentage"></div></div>
                  <span>{{ player.winPercentage | number:'1.1-1' }}%</span>
                </div>
                <div class="equity-row">
                  <span>Tie</span>
                  <div class="equity-bar"><div class="fill tie" [style.width.%]="player.tiePercentage"></div></div>
                  <span>{{ player.tiePercentage | number:'1.1-1' }}%</span>
                </div>
              </div>
            </ng-container>
          </div>
        </div>
      </section>
    </ng-container>
    </div>

    <div *ngIf="!analysis" class="waiting">
      <p>Waiting for data...</p>
    </div>
  `,
  styles: [`
    .display-container { padding: 1rem; position: relative; }
    .break-overlay {
      position: fixed; inset: 0; background: rgba(10, 12, 20, 0.92);
      display: flex; flex-direction: column; align-items: center; justify-content: center;
      z-index: 100; gap: 24px;
    }
    .break-overlay .break-label { font-size: 3rem; letter-spacing: 12px; color: #b9dceb; text-transform: uppercase; }
    .break-overlay .break-time { font-size: 12rem; font-weight: 700; font-variant-numeric: tabular-nums; color: #fff; line-height: 1; }
    .break-overlay .break-sub { font-size: 2rem; letter-spacing: 8px; color: #f0c060; }
    .muck-panel { position: absolute; top: 1rem; right: 1rem; background: #1a1a2e; border: 1px solid #333; border-radius: 6px; padding: 0.5rem 0.75rem; max-width: 320px; z-index: 10; }
    .muck-panel h3 { margin: 0 0 0.4rem 0; font-size: 0.8rem; color: #888; text-transform: uppercase; letter-spacing: 0.05em; display: flex; align-items: center; gap: 0.4rem; }
    .muck-panel .muck-count { background: #e74c3c; color: #fff; border-radius: 10px; padding: 1px 8px; font-size: 0.7rem; }
    .muck-cards { display: flex; flex-wrap: wrap; gap: 3px; }
    .card-display.small { font-size: 0.95rem; padding: 2px 5px; min-width: 26px; }
    .card-display { display: inline-flex; flex-direction: column; align-items: center; justify-content: center; }
    .card-display .rank { font-size: 2.35rem; line-height: 0.85; font-weight: 800; }
    .card-display .suit { font-size: 2.05rem; line-height: 0.85; margin-top: -2px; }
    .card-display.small .rank { font-size: 1.25rem; }
    .card-display.small .suit { font-size: 1.1rem; margin-top: 0; }
    .hole-cards .card-display .rank { font-size: 2.8rem; }
    .hole-cards .card-display .suit { font-size: 2.4rem; }
    header { display: flex; align-items: center; gap: 1rem; margin-bottom: 1.5rem; }
    h1 { font-size: 1.5rem; color: #e94560; }
    .player-count { color: #888; font-size: 0.9rem; }
    .blinds-badge { background: #0f3460; color: #ffd166; border-radius: 4px; padding: 2px 8px; font-size: 0.85rem; font-weight: 600; letter-spacing: 0.03em; }
    .chip-count { color: #ffd166; font-size: 0.8rem; margin-bottom: 0.4rem; font-weight: 600; min-height: 1em; line-height: 1; }
    .chip-count.placeholder { visibility: hidden; }
    .community-cards { text-align: center; align-self: center; }
    .community-cards h2 { margin-bottom: 0.5rem; font-size: 1rem; color: #888; }
    .cards-row { display: flex; justify-content: center; gap: 4px; }
    .card-display.empty { background: #333; color: #666; border-color: #555; }
    .table-layout { display: grid; grid-template-columns: auto minmax(0, 1fr) auto; gap: 1rem; align-items: start; }
    .players-column { display: grid; grid-auto-flow: column; grid-template-rows: repeat(3, auto); grid-auto-columns: minmax(220px, 240px); gap: 1rem; }
    .players-column.right { direction: rtl; }
    .players-column.right > * { direction: ltr; }
    .player-header { display: flex; align-items: center; gap: 0.5rem; margin-bottom: 0.5rem; }
    .seat { color: #888; font-size: 0.8rem; }
    .name { font-weight: bold; }
    .dealer-badge { background: #f39c12; color: #000; border-radius: 50%; width: 20px; height: 20px; display: inline-flex; align-items: center; justify-content: center; font-size: 0.7rem; font-weight: bold; }
    .fold-badge { background: #e74c3c; color: white; border-radius: 4px; padding: 2px 6px; font-size: 0.7rem; }
    .player-card.folded { opacity: 0.45; }
    .card-display.dim { opacity: 0.5; filter: grayscale(0.6); }
    .hole-cards { display: flex; gap: 4px; margin-bottom: 0.5rem; }
    .hand-info { margin-bottom: 0.5rem; }
    .hand-desc { color: #27ae60; font-size: 0.85rem; }
    .equity-row { display: flex; align-items: center; gap: 0.5rem; font-size: 0.75rem; margin-bottom: 2px; }
    .equity-row span:first-child { width: 28px; }
    .equity-row span:last-child { width: 40px; text-align: right; }
    .equity-bar { flex: 1; }
    .equity-bar .fill.tie { background: #f39c12; }
    .waiting { text-align: center; padding: 4rem; color: #666; }
  `]
})
export class DisplayComponent implements OnInit, OnDestroy {
  analysis: AnalysisResult | null = null;
  private sub?: Subscription;
  private breakTimer?: ReturnType<typeof setInterval>;
  private localBreakRemaining = 0;

  constructor(private analysisService: AnalysisService) {}

  ngOnInit(): void {
    this.sub = this.analysisService.analysis$.subscribe(a => {
      if (a) this.apply(a);
    });

    this.analysisService.getCurrentAnalysis().subscribe({
      next: a => this.apply(a),
      error: () => {}
    });
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
    if (this.breakTimer) clearInterval(this.breakTimer);
  }

  breakTimeDisplay(): string {
    const s = Math.max(0, this.localBreakRemaining);
    const m = Math.floor(s / 60);
    const sec = s % 60;
    return `${m}:${sec.toString().padStart(2, '0')}`;
  }

  private apply(a: AnalysisResult): void {
    this.analysis = a;
    if (a.break?.isActive) {
      this.localBreakRemaining = a.break.remainingSeconds;
      if (!this.breakTimer) {
        this.breakTimer = setInterval(() => {
          if (this.analysis?.break?.isActive && !this.analysis.break.isPaused && this.localBreakRemaining > 0) {
            this.localBreakRemaining--;
            this.analysis = { ...this.analysis };
          }
        }, 1000);
      }
    } else {
      this.localBreakRemaining = 0;
      if (this.breakTimer) { clearInterval(this.breakTimer); this.breakTimer = undefined; }
    }
  }

  getStreetName(street: number): string {
    return STREET_NAMES[street] ?? 'Unknown';
  }

  getCardLabel(card: { rank: number; suit: number }): string {
    return `${RANK_NAMES[card.rank] ?? '?'}${SUIT_SYMBOLS[card.suit] ?? ''}`;
  }

  getRank(card: { rank: number }): string { return RANK_NAMES[card.rank] ?? '?'; }
  getSuit(card: { suit: number }): string { return SUIT_SYMBOLS[card.suit] ?? ''; }

  getSuitClass(suit: number): string {
    return (SUIT_NAMES[suit] ?? '').toLowerCase();
  }

  getEmptySlots(): number[] {
    const shown = this.analysis?.communityCards?.length ?? 0;
    return Array(5 - shown).fill(0);
  }

  get allPlayers(): (PlayerAnalysis & { folded: boolean })[] {
    if (!this.analysis) return [];
    const active = (this.analysis.activePlayers ?? []).map(p => ({ ...p, folded: false }));
    const folded = (this.analysis.foldedPlayers ?? []).map(p => ({ ...p, folded: true }));
    return [...active, ...folded].sort((a, b) => a.seatNumber - b.seatNumber);
  }

  get leftPlayers(): (PlayerAnalysis & { folded: boolean })[] {
    const all = this.allPlayers;
    const half = Math.ceil(all.length / 2);
    return all.slice(0, half);
  }

  get rightPlayers(): (PlayerAnalysis & { folded: boolean })[] {
    const all = this.allPlayers;
    const half = Math.ceil(all.length / 2);
    return all.slice(half);
  }

  trackCard = (_: number, card: { rank: number; suit: number }) => `${card.rank}-${card.suit}`;
  trackPlayer = (_: number, player: { seatNumber: number }) => player.seatNumber;
  trackIndex = (i: number) => i;
}
