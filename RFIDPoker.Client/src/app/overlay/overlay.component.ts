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

      <!-- Break overlay: shown centered when a break is active. Blocks all card panels. -->
      <div class="break-panel" *ngIf="analysis.break?.isActive">
        <div class="break-label">{{ analysis.break?.label || 'BREAK' }}</div>
        <div class="break-time">{{ breakTimeDisplay() }}</div>
        <div class="break-sub" *ngIf="analysis.break?.isPaused">PAUSED</div>
      </div>

      <ng-container *ngIf="!analysis.break?.isActive">

      <!-- Tournament info (top-right): shown whenever a tournament snapshot is present. -->
      <div class="tournament-info" *ngIf="analysis.tournament as t">
        <div class="ti-row" *ngIf="t.level > 0"><span class="ti-label">LEVEL</span><span class="ti-value">{{ t.level }}</span></div>
        <div class="ti-row" *ngIf="t.playersLeft > 0"><span class="ti-label">PLAYERS</span><span class="ti-value">{{ t.playersLeft }}</span></div>
        <div class="ti-row" *ngIf="t.averageStack > 0"><span class="ti-label">AVG STACK</span><span class="ti-value">{{ t.averageStack | number }}</span></div>
        <div class="ti-row" *ngIf="t.smallBlind > 0 || t.bigBlind > 0"><span class="ti-label">BLINDS</span><span class="ti-value">{{ t.smallBlind | number }}/{{ t.bigBlind | number }}</span></div>
        <div class="ti-row ti-next" *ngIf="t.nextSmallBlind > 0 || t.nextBigBlind > 0"><span class="ti-label">NEXT</span><span class="ti-value">{{ t.nextSmallBlind | number }}/{{ t.nextBigBlind | number }}</span></div>
      </div>

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

          <div class="player-top">
            <div class="hole">
              <div *ngFor="let card of player.holeCards; trackBy: trackCard"
                   class="card hole-card"
                   [ngClass]="getSuitClass(card.suit)">
                <span class="rank">{{ getRank(card) }}</span>
                <span class="suit">{{ getSuit(card) }}</span>
              </div>
            </div>

            <div class="equity-pill"
                 *ngIf="showEquity && !player.isFolded && hasEquity(player.seatNumber)"
                 [class.dead]="getWin(player.seatNumber) <= 0 && getTie(player.seatNumber) <= 0">
              {{ getWin(player.seatNumber) | number:'1.0-0' }}%
            </div>
          </div>

          <div class="player-name">
            <span class="seat">S{{ player.seatNumber }}</span>
            <span class="name-text">{{ player.playerName }}</span>
            <span class="chip-count" *ngIf="player.chipCount != null">{{ player.chipCount | number }}</span>
            <span class="fold-tag" *ngIf="player.isFolded">FOLD</span>
          </div>

          <div class="hand-desc-strip" [class.empty]="!player.handDescription || player.isFolded">
            {{ (!player.isFolded && player.handDescription) ? player.handDescription : '\u00A0' }}
          </div>
        </div>
      </div>

      </ng-container>
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

    /* ---- Break ---- */
    .break-panel {
      position: absolute;
      top: 50%;
      left: 50%;
      transform: translate(-50%, -50%);
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 14px;
      padding: 40px 70px;
      background: rgba(10, 12, 20, 0.85);
      border: 2px solid rgba(255, 255, 255, 0.15);
      border-radius: 18px;
      box-shadow: 0 20px 60px rgba(0, 0, 0, 0.6);
    }
    .break-panel .break-label {
      font-size: 32px;
      letter-spacing: 6px;
      color: #b9dceb;
      text-transform: uppercase;
    }
    .break-panel .break-time {
      font-size: 120px;
      font-weight: 700;
      font-variant-numeric: tabular-nums;
      color: #fff;
      line-height: 1;
      text-shadow: 0 4px 20px rgba(0, 0, 0, 0.7);
    }
    .break-panel .break-sub {
      font-size: 20px;
      letter-spacing: 4px;
      color: #f0c060;
    }
    /* ---- Board ---- */
    .board-panel {
      position: absolute;
      left: 50%;
      transform: translateX(-50%);
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 10px;
      padding: 10px 18px 12px;
      background: #1c2230;
      border: 1px solid rgba(255,255,255,0.55);
      border-radius: 4px;
      box-shadow: 0 4px 14px rgba(0,0,0,0.55);
      overflow: visible;
    }
    .pos-bottom .board-panel { bottom: 40px; }
    .pos-top    .board-panel { top: 40px; }
    .pos-center .board-panel { top: 50%; transform: translate(-50%, -50%); }

    .street {
      font-size: 0.7rem;
      letter-spacing: 0.18em;
      color: #ffffff;
      text-transform: uppercase;
      font-weight: 700;
      opacity: 0.85;
    }

    .street-row {
      display: flex;
      align-items: center;
      gap: 12px;
    }
    .blinds {
      font-size: 0.72rem;
      letter-spacing: 0.05em;
      color: #ffd166;
      font-weight: 700;
      background: #2a3446;
      border: 1px solid rgba(255,255,255,0.35);
      border-radius: 3px;
      padding: 2px 8px;
    }

    /* ---- Tournament info (top-right) ---- */
    .tournament-info {
      position: absolute;
      top: 20px;
      right: 20px;
      display: flex;
      flex-direction: column;
      gap: 4px;
      padding: 10px 14px;
      background: rgba(15, 23, 32, 0.78);
      border: 1px solid rgba(255, 209, 102, 0.35);
      border-radius: 10px;
      box-shadow: 0 4px 14px rgba(0,0,0,0.5);
      backdrop-filter: blur(6px);
      min-width: 140px;
    }
    .tournament-info .ti-row {
      display: flex;
      justify-content: space-between;
      align-items: baseline;
      gap: 12px;
      font-size: 0.85rem;
    }
    .tournament-info .ti-label {
      color: #8fa4b0;
      font-size: 0.65rem;
      letter-spacing: 0.14em;
      font-weight: 700;
    }
    .tournament-info .ti-value {
      color: #ffd166;
      font-weight: 700;
    }
    .tournament-info .ti-next .ti-value { color: #e6ecef; }

    .cards-row { display: flex; gap: 6px; margin-top: 0; }

    /* ---- Card ---- */
    .card {
      width: 62px;
      height: 88px;
      background: linear-gradient(180deg, #ffffff 0%, #ececec 100%);
      color: #222;
      border-radius: 6px;
      border: 1px solid #6c7280;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      font-weight: 700;
      /* Layered shadow: crisp contact + soft ambient + drop, plus inner highlight */
      box-shadow:
        0 1px 0 rgba(255,255,255,0.85) inset,
        0 -1px 0 rgba(0,0,0,0.18) inset,
        0 1px 1px rgba(0,0,0,0.4),
        0 6px 8px rgba(0,0,0,0.55),
        0 12px 18px rgba(0,0,0,0.45);
      animation: pop 240ms ease-out;
    }
    .card .rank { font-size: 3.2rem; line-height: 0.85; font-weight: 800; }
    .card .suit { font-size: 2.9rem; line-height: 0.85; margin-top: -2px; }
    .card.hearts, .card.diamonds { color: #d0021b; }
    .card.clubs,  .card.spades   { color: #111; }
    .card.empty {
      background: rgba(255,255,255,0.06);
      border: 1px dashed rgba(255,255,255,0.22);
      box-shadow: none;
      animation: none;
    }
    .card.mini {
      width: 30px; height: 42px; border-radius: 4px;
      background: linear-gradient(180deg, #ffffff 0%, #ececec 100%);
      border: 1px solid #6c7280;
      box-shadow:
        0 1px 0 rgba(255,255,255,0.85) inset,
        0 1px 1px rgba(0,0,0,0.35),
        0 3px 6px rgba(0,0,0,0.45);
      transform: none;
    }
    .card.mini .rank { font-size: 1.05rem; }
    .card.mini .suit { font-size: 0.95rem; }

    @keyframes pop {
      from { transform: translateY(6px) scale(0.85); opacity: 0; }
      to   { transform: translateY(0)   scale(1);    opacity: 1; }
    }

    /* ---- Muck ---- */
    .muck-panel {
      position: absolute;
      top: 20px;
      right: 20px;
      background: #1c2230;
      border: 1px solid rgba(255,255,255,0.55);
      border-radius: 4px;
      padding: 8px 12px;
      max-width: 260px;
      box-shadow: 0 4px 14px rgba(0,0,0,0.55);
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
      background: #1c2230;
      border: 1px solid rgba(255,255,255,0.55);
      border-left: 3px solid #e94560;
      border-radius: 4px;
      padding: 8px 12px;
      max-width: 280px;
      box-shadow: 0 4px 14px rgba(0,0,0,0.55);
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

    /* ---- Players (WPT-style compact broadcast strip) ---- */
    .players {
      position: absolute;
      left: 20px;
      bottom: 20px;
      display: flex;
      flex-direction: column;
      gap: 16px;
      max-width: calc(100vw - 40px);
    }
    .pos-top .players { top: 20px; bottom: auto; }

    .player {
      background: #1c2230;
      border: 1px solid rgba(255,255,255,0.55);
      border-radius: 3px;
      padding: 4px 6px 0;
      width: clamp(204px, 18.7vw, 272px);
      display: flex;
      flex-direction: column;
      overflow: visible;
      box-shadow: 0 2px 6px rgba(0,0,0,0.55);
    }
    .player.folded { opacity: 0.4; }
    .player.leading .hand-desc-strip { color: #7ee8a2; }
    .player.trailing .hand-desc-strip { color: #ff9a9a; }

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

    /* Row 1: hole cards on the left, equity pill on the right. */
    .player-top {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 6px;
      /* Lift the cards so they poke above the container for a 3D pop. */
      margin-top: -10px;
      min-height: 46px;
    }
    .hole { display: flex; gap: 2px; perspective: 400px; }
    .hole .card.hole-card {
      width: 34px;
      height: 48px;
      border-radius: 4px;
      background: linear-gradient(180deg, #ffffff 0%, #ececec 100%);
      border: 1px solid #6c7280;
      /* Layered shadow: crisp contact shadow + soft ambient + subtle inner highlight */
      box-shadow:
        0 1px 0 rgba(255,255,255,0.8) inset,
        0 -1px 0 rgba(0,0,0,0.15) inset,
        0 1px 1px rgba(0,0,0,0.4),
        0 4px 6px rgba(0,0,0,0.55),
        0 8px 14px rgba(0,0,0,0.45);
      transform: translateY(-4px) rotateX(6deg);
      transform-origin: bottom center;
      animation: pop 240ms ease-out;
    }
    .hole .card.hole-card:first-child { transform: translateY(-4px) rotateX(6deg) rotateZ(-1.5deg); }
    .hole .card.hole-card:last-child  { transform: translateY(-4px) rotateX(6deg) rotateZ( 1.5deg); }
    .hole .card.hole-card .rank { font-size: 1.35rem; line-height: 0.9; font-weight: 800; }
    .hole .card.hole-card .suit { font-size: 1.2rem;  line-height: 0.9; margin-top: -1px; }

    .equity-pill {
      background: #2a3446;
      border: 1px solid rgba(255,255,255,0.35);
      border-radius: 3px;
      color: #fff;
      font-weight: 800;
      font-size: 1.3rem;
      letter-spacing: 0.02em;
      padding: 4px 10px;
      min-width: 56px;
      text-align: center;
      line-height: 1;
      align-self: stretch;
      display: flex; align-items: center; justify-content: center;
      transition: color 400ms ease;
    }
    .equity-pill.dead { color: #ff8080; }

    /* Row 2: player name. */
    .player-name {
      display: flex; align-items: baseline; gap: 6px;
      margin-top: 4px;
      color: #f2f4f8;
      font-size: 0.95rem;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.04em;
      line-height: 1.1;
    }
    .player-name .name-text { flex: 1 1 auto; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .seat {
      color: #9aa5b8;
      font-size: 0.62rem;
      font-weight: 700;
      letter-spacing: 0.08em;
    }
    .chip-count {
      color: #ffd166;
      font-size: 0.7rem;
      font-weight: 700;
      letter-spacing: 0.03em;
    }
    .fold-tag {
      background: #e74c3c; color: #fff;
      border-radius: 2px; padding: 1px 5px;
      font-size: 0.6rem; letter-spacing: 0.1em;
    }

    /* Row 3: hand description strip. */
    .hand-desc-strip {
      margin: 4px -6px 0;
      background: #2a2f3a;
      color: #e6e9f0;
      font-size: 0.78rem;
      font-weight: 600;
      letter-spacing: 0.04em;
      text-transform: uppercase;
      padding: 3px 6px;
      min-height: 1.1em;
      border-top: 1px solid rgba(255,255,255,0.08);
      transition: color 400ms ease;
    }
    .hand-desc-strip.empty { color: transparent; }

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
  private breakTimer?: ReturnType<typeof setInterval>;
  private localBreakRemaining = 0;

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

  private prevHtmlBackground: string | null = null;
  private prevBodyBackground: string | null = null;

  constructor(
    private analysisService: AnalysisService,
    private route: ActivatedRoute
  ) {}

  ngOnInit(): void {
    // Force page-level transparency so OBS can composite the camera feed.
    // Save prior values so we can restore them on destroy (otherwise navigating
    // back to Manage/Display leaves the whole page blank/white).
    this.prevHtmlBackground = document.documentElement.style.background;
    this.prevBodyBackground = document.body.style.background;
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
    if (this.breakTimer) clearInterval(this.breakTimer);
    // Restore page background so other routes render normally.
    document.documentElement.style.background = this.prevHtmlBackground ?? '';
    document.body.style.background = this.prevBodyBackground ?? '';
  }

  breakTimeDisplay(): string {
    const s = Math.max(0, this.localBreakRemaining);
    const m = Math.floor(s / 60);
    const sec = s % 60;
    return `${m}:${sec.toString().padStart(2, '0')}`;
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

    // Sync break countdown: server sends authoritative remainingSeconds; we tick locally between broadcasts.
    if (a.break?.isActive) {
      this.localBreakRemaining = a.break.remainingSeconds;
      if (!this.breakTimer) {
        this.breakTimer = setInterval(() => {
          if (this.analysis?.break?.isActive && !this.analysis.break.isPaused && this.localBreakRemaining > 0) {
            this.localBreakRemaining--;
            // Trigger change detection.
            this.analysis = { ...this.analysis };
          }
        }, 1000);
      }
    } else {
      this.localBreakRemaining = 0;
      if (this.breakTimer) { clearInterval(this.breakTimer); this.breakTimer = undefined; }
    }
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
