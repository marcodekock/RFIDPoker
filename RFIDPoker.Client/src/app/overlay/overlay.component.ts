import { Component, HostBinding, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { Subscription } from 'rxjs';
import { AnalysisService } from '../services/analysis.service';
import {
  AnalysisResult,
  RANK_NAMES,
  SUIT_NAMES,
  SUIT_SYMBOLS,
  STREET_NAMES
} from '../models';

/**
 * OBS Browser Source overlay.
 *
 * Add to OBS as a Browser Source pointing at e.g.:
 *   http://localhost:4200/overlay
 *   http://localhost:4200/overlay?board=1&hole=1&equity=1&muck=1&street=1&position=bottom
 *
 * The page background is fully transparent so the underlying camera feed
 * (placed BELOW this browser source in the OBS scene) shows through.
 *
 * Query params (all optional, 1/0):
 *   board    - show community cards       (default 1)
 *   hole     - show player hole cards     (default 1)
 *   equity   - show win/tie % bars        (default 0)
 *   muck     - show mucked cards          (default 0)
 *   street   - show street badge          (default 1)
 *   folded   - show folded players        (default 0)
 *   position - board position: bottom | top | center (default bottom)
 *   scale    - scale factor, e.g. 1.25    (default 1)
 */
@Component({
  selector: 'app-overlay',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="overlay-root" *ngIf="analysis"
         [class.pos-bottom]="position === 'bottom'"
         [class.pos-top]="position === 'top'"
         [class.pos-center]="position === 'center'"
         [style.--scale]="scale">

      <!-- Heads-up outs tab (top-left, only when 2 players remain on flop/turn) -->
      <div class="outs-panel" *ngIf="analysis.headsUpOuts as ho">
        <div class="outs-header">
          <span class="outs-label">OUTS</span>
          <span class="outs-name">{{ ho.playerName }}</span>
          <span class="outs-count">{{ ho.outs.length }}</span>
        </div>
        <div class="outs-cards">
          <div *ngFor="let card of ho.outs; trackBy: trackCard"
               class="card mini out-card"
               [ngClass]="getSuitClass(card.suit)">
            <span class="rank">{{ getRank(card) }}</span>
            <span class="suit">{{ getSuit(card) }}</span>
          </div>
        </div>
      </div>

      <!-- Board / community cards -->
      <div class="board-panel" *ngIf="showBoard">
        <div class="street-row">
          <div class="street" *ngIf="showStreet">{{ getStreetName(analysis.currentStreet) }}</div>
          <div class="blinds" *ngIf="analysis.blinds">{{ analysis.blinds }}</div>
        </div>
        <div class="cards-row">
          <div *ngFor="let card of analysis.communityCards; trackBy: trackCard"
               class="card"
               [ngClass]="getSuitClass(card.suit)">
            <span class="rank">{{ getRank(card) }}</span>
            <span class="suit">{{ getSuit(card) }}</span>
          </div>
          <div *ngFor="let slot of getEmptySlots(); trackBy: trackIndex" class="card empty"></div>
        </div>
      </div>

      <!-- Muck (mucked / folded cards) - always shown when non-empty -->
      <div class="muck-panel" *ngIf="showMuck && analysis.muckedCards?.length">
        <div class="muck-label">MUCK <span class="muck-count">{{ analysis.muckedCards.length }}</span></div>
        <div class="muck-cards">
          <div *ngFor="let card of analysis.muckedCards; trackBy: trackCard"
               class="card mini muck-card"
               [ngClass]="getSuitClass(card.suit)">
            <span class="rank">{{ getRank(card) }}</span>
            <span class="suit">{{ getSuit(card) }}</span>
          </div>
        </div>
      </div>

      <!-- Players -->
      <div class="players" *ngIf="showHole">
        <div *ngFor="let player of getPlayersToShow(); trackBy: trackPlayer"
             class="player"
             [class.folded]="player.isFolded"
             [class.leading]="isLeading(player.seatNumber)"
             [class.trailing]="isTrailing(player.seatNumber)">
          <div class="player-name">
            <span class="seat">S{{ player.seatNumber }}</span>
            {{ player.playerName }}
            <span class="fold-tag" *ngIf="player.isFolded">FOLD</span>
          </div>
          <div class="chip-count" [class.placeholder]="player.chipCount == null">
            <span *ngIf="player.chipCount != null; else emptyChips">{{ player.chipCount | number }}</span>
            <ng-template #emptyChips><span>&nbsp;</span></ng-template>
          </div>
          <div class="hole">
            <div *ngFor="let card of player.holeCards; trackBy: trackCard"
                 class="card"
                 [ngClass]="getSuitClass(card.suit)">
              <span class="rank">{{ getRank(card) }}</span>
              <span class="suit">{{ getSuit(card) }}</span>
            </div>
          </div>
          <div class="hand-desc" *ngIf="player.handDescription && !player.isFolded">
            {{ player.handDescription }}
          </div>
          <div class="equity" *ngIf="showEquity && !player.isFolded && hasEquity(player.seatNumber)">
            <div class="bar">
              <div class="fill win" [style.width.%]="getWin(player.seatNumber)"></div>
              <div class="fill tie" [style.width.%]="getTie(player.seatNumber)"
                   [style.left.%]="getWin(player.seatNumber)"></div>
            </div>
            <div class="pct">
              <span class="win-pct fade-swap"
                    [class.dead]="getWin(player.seatNumber) <= 0 && getTie(player.seatNumber) <= 0"
                    [attr.data-value]="getWin(player.seatNumber) | number:'1.0-1'">
                {{ getWin(player.seatNumber) | number:'1.0-1' }}%
              </span>
              <span class="tie-pct fade-swap" *ngIf="getTie(player.seatNumber) >= 0.1"
                    [attr.data-value]="getTie(player.seatNumber) | number:'1.0-1'">
                +{{ getTie(player.seatNumber) | number:'1.0-1' }}%
              </span>
            </div>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    :host {
      display: block;
      width: 100vw;
      height: 100vh;
      background: transparent;
      color: #fff;
      font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
      overflow: hidden;
      user-select: none;
      -webkit-font-smoothing: antialiased;
    }

    .overlay-root {
      position: absolute;
      inset: 0;
      pointer-events: none;
      transform: scale(var(--scale, 1));
      transform-origin: center bottom;
    }

    /* ---- Board ---- */
    .board-panel {
      position: absolute;
      left: 50%;
      transform: translateX(-50%);
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 8px;
      padding: 14px 22px;
      background: rgba(10, 12, 20, 0.72);
      border: 1px solid rgba(233, 69, 96, 0.55);
      border-radius: 14px;
      box-shadow: 0 6px 24px rgba(0, 0, 0, 0.55);
      backdrop-filter: blur(6px);
    }
    .pos-bottom .board-panel { bottom: 40px; }
    .pos-top    .board-panel { top: 40px; }
    .pos-center .board-panel { top: 50%; transform: translate(-50%, -50%); }

    .street {
      font-size: 0.75rem;
      letter-spacing: 0.18em;
      color: #e94560;
      text-transform: uppercase;
      font-weight: 700;
    }

    .street-row {
      display: flex;
      align-items: center;
      gap: 12px;
    }
    .blinds {
      font-size: 0.8rem;
      letter-spacing: 0.05em;
      color: #ffd166;
      font-weight: 700;
      background: rgba(255, 209, 102, 0.12);
      border: 1px solid rgba(255, 209, 102, 0.4);
      border-radius: 4px;
      padding: 2px 8px;
    }

    .cards-row { display: flex; gap: 8px; }

    /* ---- Card ---- */
    .card {
      width: 62px;
      height: 88px;
      background: #fff;
      color: #222;
      border-radius: 8px;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      font-weight: 700;
      box-shadow: 0 2px 6px rgba(0,0,0,0.5);
      animation: pop 240ms ease-out;
    }
    .card .rank { font-size: 1.55rem; line-height: 1; }
    .card .suit { font-size: 1.35rem; line-height: 1; margin-top: 2px; }
    .card.hearts, .card.diamonds { color: #d0021b; }
    .card.clubs,  .card.spades   { color: #111; }
    .card.empty {
      background: rgba(255,255,255,0.08);
      border: 2px dashed rgba(255,255,255,0.2);
      box-shadow: none;
      animation: none;
    }
    .card.mini { width: 30px; height: 42px; border-radius: 4px; }
    .card.mini .rank { font-size: 0.8rem; }
    .card.mini .suit { font-size: 0.7rem; }

    @keyframes pop {
      from { transform: translateY(6px) scale(0.85); opacity: 0; }
      to   { transform: translateY(0)   scale(1);    opacity: 1; }
    }

    /* ---- Muck ---- */
    .muck-panel {
      position: absolute;
      top: 20px;
      right: 20px;
      background: rgba(10, 12, 20, 0.72);
      border: 1px solid rgba(255,255,255,0.15);
      border-radius: 10px;
      padding: 8px 12px;
      max-width: 260px;
    }
    .muck-card {
      animation: muckIn 350ms ease;
      filter: grayscale(0.35);
    }
    @keyframes muckIn {
      from { opacity: 0; transform: translateY(-4px) scale(0.85); }
      to   { opacity: 1; transform: translateY(0)    scale(1); }
    }

    /* ---- Heads-up outs ---- */
    .outs-panel {
      position: absolute;
      top: 20px;
      left: 20px;
      background: rgba(10, 12, 20, 0.78);
      border: 1px solid rgba(255,255,255,0.18);
      border-left: 3px solid #e74c3c;
      border-radius: 10px;
      padding: 8px 12px;
      max-width: 280px;
      animation: fadeIn 400ms ease;
    }
    .outs-header {
      display: flex;
      align-items: center;
      gap: 8px;
      margin-bottom: 6px;
      font-size: 0.78rem;
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }
    .outs-label { color: #e74c3c; font-weight: 700; }
    .outs-name { color: #fff; font-weight: 600; }
    .outs-count {
      margin-left: auto;
      background: rgba(231, 76, 60, 0.25);
      color: #fff;
      padding: 1px 8px;
      border-radius: 10px;
      font-weight: 700;
    }
    .outs-cards {
      display: flex;
      flex-wrap: wrap;
      gap: 4px;
    }
    .out-card {
      animation: muckIn 300ms ease;
    }
    .muck-label {
      font-size: 0.65rem;
      letter-spacing: 0.15em;
      color: #aaa;
      margin-bottom: 6px;
      display: flex; align-items: center; gap: 6px;
    }
    .muck-count {
      background: #e74c3c; color: #fff;
      padding: 1px 8px; border-radius: 10px;
      font-size: 0.65rem;
    }
    .muck-cards { display: flex; flex-wrap: wrap; gap: 4px; }

    /* ---- Players ---- */
    .players {
      position: absolute;
      left: 20px;
      bottom: 20px;
      display: grid;
      grid-auto-flow: column;
      grid-template-rows: repeat(3, auto);
      grid-auto-columns: minmax(150px, max-content);
      gap: 10px;
      max-width: calc(100vw - 40px);
    }
    .pos-top .players { top: 20px; bottom: auto; }

    .player {
      background: rgba(10, 12, 20, 0.72);
      border: 1px solid rgba(255,255,255,0.12);
      border-radius: 10px;
      padding: 8px 10px;
      min-width: 150px;
    }
    .player.folded { opacity: 0.4; }
    .player.leading .hand-desc { color: #2ecc71; }
    .player.trailing .hand-desc { color: #e74c3c; }

    /* Fold sequence: gray + desaturate, then fade + shrink away. */
    .player.folded {
      animation: foldOut 2500ms ease forwards;
    }
    @keyframes foldOut {
      0%   { opacity: 1;   filter: grayscale(0); transform: scale(1); }
      15%  { opacity: 0.8; filter: grayscale(1); transform: scale(1); }
      70%  { opacity: 0.5; filter: grayscale(1); transform: scale(0.98); }
      100% { opacity: 0;   filter: grayscale(1); transform: scale(0.9); }
    }
    .player.folded .card { box-shadow: none; }

    .player-name {
      font-size: 0.8rem;
      display: flex; align-items: center; gap: 6px;
      margin-bottom: 6px;
    }
    .seat { color: #888; font-size: 0.7rem; }
    .fold-tag {
      background: #e74c3c; color: #fff;
      border-radius: 3px; padding: 1px 5px;
      font-size: 0.6rem; letter-spacing: 0.1em;
    }

    .chip-count {
      color: #ffd166;
      font-size: 0.75rem;
      font-weight: 700;
      margin-bottom: 4px;
      min-height: 1em;
      line-height: 1;
    }
    .chip-count.placeholder { visibility: hidden; }

    .hole { display: flex; gap: 4px; }
    .hole .card { width: 40px; height: 56px; border-radius: 5px; }
    .hole .card .rank { font-size: 1rem; }
    .hole .card .suit { font-size: 0.9rem; }

    .hand-desc {
      margin-top: 6px;
      color: #27ae60;
      font-size: 0.75rem;
      font-weight: 600;
      transition: color 400ms ease;
    }

    .equity { margin-top: 6px; width: 100%; }
    .equity .bar {
      position: relative;
      height: 6px;
      border-radius: 3px;
      background: rgba(255,255,255,0.12);
      overflow: hidden;
    }
    .equity .fill {
      position: absolute;
      top: 0; bottom: 0;
      left: 0;
      transition: width 500ms ease, left 500ms ease;
    }
    .equity .fill.win { background: #27ae60; }
    .equity .fill.tie { background: #f39c12; }
    .equity .pct {
      display: flex; gap: 6px;
      font-size: 0.7rem; margin-top: 3px;
    }
    .equity .win-pct { color: #2ecc71; font-weight: 600; transition: color 400ms ease; }
    .equity .win-pct.dead { color: #e74c3c; }
    .equity .tie-pct { color: #f39c12; }

    /* Fade the whole equity block in the first time it appears for this player. */
    .equity {
      animation: fadeIn 500ms ease;
    }
    @keyframes fadeIn {
      from { opacity: 0; }
      to   { opacity: 1; }
    }
  `]
})
export class OverlayComponent implements OnInit, OnDestroy {
  @HostBinding('style.background') bg = 'transparent';

  analysis: AnalysisResult | null = null;
  showBoard = true;
  showHole = true;
  showEquity = true;
  showMuck = true;
  showStreet = true;
  showFolded = true;
  position: 'bottom' | 'top' | 'center' = 'bottom';
  scale = 1;

  private sub?: Subscription;

  // Latched equity per seat. Kept between broadcasts so the interim update
  // (which arrives with 0% equity while the calculator runs) doesn't blank
  // the bar. We only overwrite when the new broadcast reports non-zero equity,
  // or when the seat no longer has hole cards (hand reset).
  private equityBySeat = new Map<number, { win: number; tie: number }>();

  /** Seats currently in their post-fold gray-out + fade-away animation. Value is the timer id. */
  private recentlyFolded = new Map<number, ReturnType<typeof setTimeout>>();
  /** Seats we've already seen as folded — used to detect the "newly folded" transition. */
  private knownFolded = new Set<number>();

  private static readonly FOLD_FADE_MS = 2500;

  constructor(
    private analysisService: AnalysisService,
    private route: ActivatedRoute
  ) {}

  ngOnInit(): void {
    // Force page-level transparency so OBS can composite the camera feed.
    document.documentElement.style.background = 'transparent';
    document.body.style.background = 'transparent';

    const qp = this.route.snapshot.queryParamMap;
    this.showBoard  = this.flag(qp.get('board'),  true);
    this.showHole   = this.flag(qp.get('hole'),   true);
    this.showEquity = this.flag(qp.get('equity'), true);
    this.showMuck   = this.flag(qp.get('muck'),   true);
    this.showStreet = this.flag(qp.get('street'), true);
    this.showFolded = this.flag(qp.get('folded'), true);
    const pos = qp.get('position');
    if (pos === 'top' || pos === 'center' || pos === 'bottom') this.position = pos;
    const s = parseFloat(qp.get('scale') ?? '');
    if (!isNaN(s) && s > 0.1 && s < 5) this.scale = s;

    this.sub = this.analysisService.analysis$.subscribe(a => {
      if (a) this.applyAnalysis(a);
    });

    this.analysisService.getCurrentAnalysis().subscribe({
      next: a => this.applyAnalysis(a),
      error: () => {}
    });
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
  }

  private flag(v: string | null, def: boolean): boolean {
    if (v === null) return def;
    return v === '1' || v.toLowerCase() === 'true';
  }

  getPlayersToShow() {
    if (!this.analysis) return [];
    const active = this.analysis.activePlayers ?? [];
    // Only show folded players who are still in their fade-out window; after that
    // they're dropped so the row doesn't stay cluttered for the rest of the hand.
    const folded = (this.analysis.foldedPlayers ?? [])
      .filter(p => this.recentlyFolded.has(p.seatNumber));
    return [...active, ...folded];
  }

  isFading(seat: number): boolean {
    return this.recentlyFolded.has(seat);
  }

  getStreetName(street: number): string { return STREET_NAMES[street] ?? ''; }
  getRank(card: { rank: number }): string { return RANK_NAMES[card.rank] ?? '?'; }
  getSuit(card: { suit: number }): string { return SUIT_SYMBOLS[card.suit] ?? ''; }
  getSuitClass(suit: number): string { return (SUIT_NAMES[suit] ?? '').toLowerCase(); }

  getEmptySlots(): number[] {
    const shown = this.analysis?.communityCards?.length ?? 0;
    return Array(Math.max(0, 5 - shown)).fill(0);
  }

  // Stable identities so Angular reuses DOM nodes across SignalR updates instead of
  // tearing them down and replaying the pop animation on every broadcast.
  trackCard = (_: number, card: { rank: number; suit: number }) => `${card.rank}-${card.suit}`;
  trackPlayer = (_: number, player: { seatNumber: number }) => player.seatNumber;
  trackIndex = (i: number) => i;

  private applyAnalysis(a: AnalysisResult): void {
    // Detect newly-folded seats and start (or preserve) their fade-out timer.
    const foldedNow = new Set<number>((a.foldedPlayers ?? []).map(p => p.seatNumber));
    for (const seat of foldedNow) {
      if (!this.knownFolded.has(seat) && !this.recentlyFolded.has(seat)) {
        const t = setTimeout(() => {
          this.recentlyFolded.delete(seat);
          // Nudge change detection: reassign analysis reference.
          this.analysis = this.analysis ? { ...this.analysis } : this.analysis;
        }, OverlayComponent.FOLD_FADE_MS);
        this.recentlyFolded.set(seat, t);
      }
    }
    // Seats that came back (new hand, unfold): clear any lingering fade state.
    for (const seat of Array.from(this.recentlyFolded.keys())) {
      if (!foldedNow.has(seat)) {
        clearTimeout(this.recentlyFolded.get(seat)!);
        this.recentlyFolded.delete(seat);
      }
    }
    this.knownFolded = foldedNow;

    // Seats still holding cards this hand — anything else is either sat out or between hands.
    const liveSeats = new Set<number>();
    const all = [...(a.activePlayers ?? []), ...(a.foldedPlayers ?? [])];
    for (const p of all) {
      if (p.holeCards?.length) liveSeats.add(p.seatNumber);
    }

    // Drop latched equity for seats that no longer have cards (new hand / player left).
    for (const seat of Array.from(this.equityBySeat.keys())) {
      if (!liveSeats.has(seat)) this.equityBySeat.delete(seat);
    }

    // Detect whether this broadcast contains a real equity calculation. The interim
    // update publishes with all zeros; the final update has at least one non-zero
    // percentage. When it's authoritative we overwrite EVERY active seat (including
    // zeros — e.g. a player who's drawing dead against a made hand), otherwise we
    // leave the latch untouched so the previous values stay on screen.
    const hasAuthoritativeEquity = (a.activePlayers ?? [])
      .some(p => p.winPercentage > 0 || p.tiePercentage > 0);

    if (hasAuthoritativeEquity) {
      for (const p of a.activePlayers ?? []) {
        this.equityBySeat.set(p.seatNumber, {
          win: p.winPercentage,
          tie: p.tiePercentage
        });
      }
    }

    this.analysis = a;
  }

  hasEquity(seat: number): boolean {
    return this.equityBySeat.has(seat);
  }
  getWin(seat: number): number { return this.equityBySeat.get(seat)?.win ?? 0; }
  getTie(seat: number): number { return this.equityBySeat.get(seat)?.tie ?? 0; }

  /** Highest win% across all latched seats, or 0 if none. */
  private maxWin(): number {
    let m = 0;
    for (const eq of this.equityBySeat.values()) if (eq.win > m) m = eq.win;
    return m;
  }

  /** True if this seat has the (uniquely) highest win% among all latched seats. */
  isLeading(seat: number): boolean {
    const eq = this.equityBySeat.get(seat);
    if (!eq || eq.win <= 0) return false;
    const top = this.maxWin();
    if (eq.win < top) return false;
    // Only highlight if strictly ahead — avoid painting everyone green on a coin flip.
    let count = 0;
    for (const other of this.equityBySeat.values()) if (other.win === top) count++;
    return count === 1;
  }

  /** True if this seat is behind at least one other latched seat. */
  isTrailing(seat: number): boolean {
    const eq = this.equityBySeat.get(seat);
    if (!eq) return false;
    if (this.equityBySeat.size < 2) return false;
    return eq.win < this.maxWin();
  }
}
