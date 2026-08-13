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
      <header>
        <h1>RFID Poker</h1>
        <span class="street-badge">{{ getStreetName(analysis.currentStreet) }}</span>
        <span class="player-count">{{ analysis.activePlayerCount }} players</span>
      </header>

      <section class="community-cards">
        <h2>Board</h2>
        <div class="cards-row">
          <div *ngFor="let card of analysis.communityCards"
               class="card-display"
               [ngClass]="getSuitClass(card.suit)">
            {{ getCardLabel(card) }}
          </div>
          <div *ngFor="let _ of getEmptySlots()" class="card-display empty">?</div>
        </div>
      </section>

      <section class="players-grid">
        <div *ngFor="let player of analysis.activePlayers"
             class="player-card"
             [class.dealer]="player.isDealer">
          <div class="player-header">
            <span class="seat">Seat {{ player.seatNumber }}</span>
            <span class="name">{{ player.playerName }}</span>
            <span *ngIf="player.isDealer" class="dealer-badge">D</span>
          </div>

          <div class="hole-cards">
            <div *ngFor="let card of player.holeCards"
                 class="card-display"
                 [ngClass]="getSuitClass(card.suit)">
              {{ getCardLabel(card) }}
            </div>
          </div>

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
        </div>

        <div *ngFor="let player of analysis.foldedPlayers"
             class="player-card folded">
          <div class="player-header">
            <span class="seat">Seat {{ player.seatNumber }}</span>
            <span class="name">{{ player.playerName }}</span>
            <span class="fold-badge">FOLDED</span>
          </div>
        </div>
      </section>
    </div>

    <div *ngIf="!analysis" class="waiting">
      <p>Waiting for data...</p>
    </div>
  `,
  styles: [`
    .display-container { padding: 1rem; }
    header { display: flex; align-items: center; gap: 1rem; margin-bottom: 1.5rem; }
    h1 { font-size: 1.5rem; color: #e94560; }
    .player-count { color: #888; font-size: 0.9rem; }
    .community-cards { margin-bottom: 2rem; text-align: center; }
    .community-cards h2 { margin-bottom: 0.5rem; font-size: 1rem; color: #888; }
    .cards-row { display: flex; justify-content: center; gap: 4px; }
    .card-display.empty { background: #333; color: #666; border-color: #555; }
    .players-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); gap: 1rem; }
    .player-header { display: flex; align-items: center; gap: 0.5rem; margin-bottom: 0.5rem; }
    .seat { color: #888; font-size: 0.8rem; }
    .name { font-weight: bold; }
    .dealer-badge { background: #f39c12; color: #000; border-radius: 50%; width: 20px; height: 20px; display: inline-flex; align-items: center; justify-content: center; font-size: 0.7rem; font-weight: bold; }
    .fold-badge { background: #e74c3c; color: white; border-radius: 4px; padding: 2px 6px; font-size: 0.7rem; }
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

  constructor(private analysisService: AnalysisService) {}

  ngOnInit(): void {
    this.sub = this.analysisService.analysis$.subscribe(a => {
      if (a) this.analysis = a;
    });

    this.analysisService.getCurrentAnalysis().subscribe({
      next: a => this.analysis = a,
      error: () => {}
    });
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
  }

  getStreetName(street: number): string {
    return STREET_NAMES[street] ?? 'Unknown';
  }

  getCardLabel(card: { rank: number; suit: number }): string {
    return `${RANK_NAMES[card.rank] ?? '?'}${SUIT_SYMBOLS[card.suit] ?? ''}`;
  }

  getSuitClass(suit: number): string {
    return (SUIT_NAMES[suit] ?? '').toLowerCase();
  }

  getEmptySlots(): number[] {
    const shown = this.analysis?.communityCards?.length ?? 0;
    return Array(5 - shown).fill(0);
  }
}
