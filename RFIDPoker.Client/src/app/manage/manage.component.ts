import { Component, OnDestroy, OnInit, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { Subscription, interval } from 'rxjs';
import { AnalysisService } from '../services/analysis.service';
import { AuthService } from '../services/auth.service';
import { BroadcastService } from '../services/broadcast.service';
import { BreakState } from '../models';
import { ToastrService } from 'ngx-toastr';

interface PlayerRow {
  seatNumber: number;
  name: string;
  chipCount: number | null;
}

interface ManualTdInfo {
  level: number;
  playersLeft: number;
  totalChips: number;
  smallBlind: number;
  bigBlind: number;
  nextSmallBlind: number;
  nextBigBlind: number;
}

@Component({
  selector: 'app-manage',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './manage.component.html',
  styleUrls: ['./manage.component.css']
})
export class ManageComponent implements OnInit, OnDestroy {
  players = signal<PlayerRow[]>([]);
  blinds = signal<string>('');
  breakDurationMinutes = signal<number>(15);
  breakLabel = signal<string>('Break');
  breakState = signal<BreakState | null>(null);
  tdEnabled = signal<boolean>(false);
  tdIntegrationEnabled = signal<boolean>(false);
  tdHasToken = signal<boolean>(false);
  tdBusy = signal<boolean>(false);
  savingBlinds = signal(false);
  savingPlayers = signal(false);
  manualTd = signal<ManualTdInfo>({
    level: 0, playersLeft: 0, totalChips: 0, smallBlind: 0, bigBlind: 0, nextSmallBlind: 0, nextBigBlind: 0
  });
  savingManualTd = signal(false);

  private sub?: Subscription;
  private tick?: Subscription;
  localRemaining = signal<number>(0);

  remainingDisplay = computed(() => {
    const s = this.localRemaining();
    const m = Math.floor(s / 60);
    const sec = s % 60;
    return `${m}:${sec.toString().padStart(2, '0')}`;
  });

  constructor(private http: HttpClient, private analysis: AnalysisService, private auth: AuthService, public broadcast: BroadcastService, private toastr: ToastrService) {}

  async toggleBroadcast() {
    try {
      if (this.broadcast.isLive()) {
        await this.broadcast.stop();
        this.toastr.warning('RFID reads are now ignored.', 'Broadcast stopped');
      } else {
        await this.broadcast.start();
        this.toastr.success('Table is live.', 'Broadcast started');
      }
    } catch {
      this.toastr.error('Failed to change broadcast state.');
    }
  }

  loadTdStatus() {
    this.http.get<any>('/api/tournament-director/status').subscribe({
      next: s => {
        this.tdIntegrationEnabled.set(!!s?.enabled);
        this.tdHasToken.set(!!s?.hasActiveToken);
      },
      error: () => {}
    });
  }

  loadManualTd() {
    this.http.get<any>('/api/tournament/manual-info').subscribe({
      next: v => {
        if (v) this.manualTd.set({
          level: v.level ?? 0,
          playersLeft: v.playersLeft ?? 0,
          totalChips: v.totalChips ?? 0,
          smallBlind: v.smallBlind ?? 0,
          bigBlind: v.bigBlind ?? 0,
          nextSmallBlind: v.nextSmallBlind ?? 0,
          nextBigBlind: v.nextBigBlind ?? 0
        });
      },
      error: () => {}
    });
  }

  patchManualTd(patch: Partial<ManualTdInfo>) {
    this.manualTd.update(v => ({ ...v, ...patch }));
  }

  saveManualTd() {
    this.savingManualTd.set(true);
    this.http.put('/api/tournament/manual-info', this.manualTd()).subscribe({
      next: () => { this.savingManualTd.set(false); this.toastr.success('Tournament info saved'); },
      error: () => { this.savingManualTd.set(false); this.toastr.error('Failed to save tournament info'); }
    });
  }

  clearManualTd() {
    this.manualTd.set({ level: 0, playersLeft: 0, totalChips: 0, smallBlind: 0, bigBlind: 0, nextSmallBlind: 0, nextBigBlind: 0 });
    this.saveManualTd();
  }

  toggleTdIntegration() {
    const willEnable = !this.tdIntegrationEnabled();
    if (willEnable && !this.tdHasToken()) {
      this.toastr.warning('Generate a Tournament Director token on the Tokens page before enabling the integration.', 'No active token');
      return;
    }
    this.tdBusy.set(true);
    this.http.put('/api/tournament-director/settings', { enabled: willEnable }).subscribe({
      next: () => {
        this.tdBusy.set(false);
        this.tdIntegrationEnabled.set(willEnable);
        if (willEnable) {
          this.toastr.success('Tournament Director integration enabled.');
        } else {
          this.toastr.warning('Tournament Director integration disabled.');
        }
      },
      error: (err) => {
        this.tdBusy.set(false);
        this.toastr.error(err?.error?.message ?? 'Failed to update Tournament Director integration.');
        this.loadTdStatus();
      }
    });
  }

  ngOnInit(): void {
    this.loadPlayers();
    this.loadBlinds();
    this.loadBreak();
    this.loadTdStatus();
    this.loadManualTd();

    this.sub = this.analysis.analysis$.subscribe(r => {
      if (!r) return;
      this.tdEnabled.set(!!r.tournament);
      // Keep blinds/break state in sync with server broadcasts (do not stomp
      // player rows the user might be editing).
      if (r.break) {
        this.breakState.set(r.break);
        this.localRemaining.set(r.break.remainingSeconds);
      } else {
        this.breakState.set(null);
        this.localRemaining.set(0);
      }
    });

    // Local 1s decrement so the countdown looks smooth between broadcasts.
    this.tick = interval(1000).subscribe(() => {
      const b = this.breakState();
      if (b && b.isActive && !b.isPaused) {
        const r = this.localRemaining();
        if (r > 0) this.localRemaining.set(r - 1);
      }
    });
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
    this.tick?.unsubscribe();
  }

  private loadPlayers() {
    this.http.get<PlayerRow[]>('/api/players').subscribe(rows => {
      const bySeat = new Map(rows.map(r => [r.seatNumber, r]));
      const full: PlayerRow[] = [];
      for (let seat = 1; seat <= 9; seat++) {
        const existing = bySeat.get(seat);
        full.push(existing ?? { seatNumber: seat, name: `Player ${seat}`, chipCount: null });
      }
      this.players.set(full);
    });
  }

  private loadBlinds() {
    this.http.get<{ blinds: string | null }>('/api/table/blinds').subscribe(r => {
      this.blinds.set(r.blinds ?? '');
    });
  }

  private loadBreak() {
    this.http.get<BreakState | null>('/api/tournament/break').subscribe(b => {
      this.breakState.set(b);
      this.localRemaining.set(b?.remainingSeconds ?? 0);
    });
  }

  savePlayers() {
    this.savingPlayers.set(true);
    const payload = {
      players: this.players().map(p => ({
        seatNumber: p.seatNumber,
        name: p.name,
        chipCount: p.chipCount === null || (p.chipCount as any) === '' ? null : Number(p.chipCount)
      }))
    };
    this.http.put('/api/players', payload).subscribe({
      next: () => { this.savingPlayers.set(false); this.flash('Players saved'); },
      error: e => { this.savingPlayers.set(false); this.flash('Error: ' + (e.error || e.message)); }
    });
  }

  savePlayer(row: PlayerRow) {
    this.http.put(`/api/players/${row.seatNumber}/name`, { name: row.name }).subscribe();
    this.http.put(`/api/players/${row.seatNumber}/chips`, {
      chipCount: row.chipCount === null || (row.chipCount as any) === '' ? null : Number(row.chipCount)
    }).subscribe();
  }

  clearChips() {
    for (const p of this.players()) {
      this.http.put(`/api/players/${p.seatNumber}/chips`, { chipCount: null }).subscribe();
    }
    this.players.update(list => list.map(p => ({ ...p, chipCount: null })));
    this.flash('Chip counts cleared');
  }

  saveBlinds() {
    this.savingBlinds.set(true);
    this.http.put('/api/table/blinds', { blinds: this.blinds() || null }).subscribe({
      next: () => { this.savingBlinds.set(false); this.flash('Blinds saved'); },
      error: () => this.savingBlinds.set(false)
    });
  }

  clearBlinds() {
    this.http.delete('/api/table/blinds').subscribe(() => {
      this.blinds.set('');
      this.flash('Blinds cleared');
    });
  }

  newHand() {
    this.http.post('/api/tournament/new-hand', {}).subscribe(() => this.flash('New hand started'));
  }

  startBreak() {
    const seconds = Math.max(1, Math.floor(this.breakDurationMinutes() * 60));
    this.http.post<BreakState>('/api/tournament/break/start', {
      durationSeconds: seconds,
      label: this.breakLabel() || null
    }).subscribe(b => {
      this.breakState.set(b);
      this.localRemaining.set(b.remainingSeconds);
    });
  }

  quickBreak(minutes: number) {
    this.breakDurationMinutes.set(minutes);
    this.startBreak();
  }

  pauseBreak() {
    this.http.post<BreakState>('/api/tournament/break/pause', {}).subscribe(b => this.breakState.set(b));
  }

  resumeBreak() {
    this.http.post<BreakState>('/api/tournament/break/resume', {}).subscribe(b => {
      this.breakState.set(b);
      if (b) this.localRemaining.set(b.remainingSeconds);
    });
  }

  adjustBreak(delta: number) {
    this.http.post<BreakState>('/api/tournament/break/adjust', { deltaSeconds: delta })
      .subscribe(b => {
        this.breakState.set(b);
        if (b) this.localRemaining.set(b.remainingSeconds);
      });
  }

  stopBreak() {
    this.http.post('/api/tournament/break/stop', {}).subscribe(() => {
      this.breakState.set(null);
      this.localRemaining.set(0);
    });
  }

  private flash(msg: string) {
    if (msg.toLowerCase().startsWith('error')) {
      this.toastr.error(msg);
    } else {
      this.toastr.success(msg);
    }
  }

  trackBySeat = (_: number, p: PlayerRow) => p.seatNumber;

  logout() { this.auth.logout(); }
}
