import { Component, OnDestroy, OnInit, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { Subscription, interval } from 'rxjs';
import { AnalysisService } from '../services/analysis.service';
import { BreakState } from '../models';

interface PlayerRow {
  seatNumber: number;
  name: string;
  chipCount: number | null;
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
  savingBlinds = signal(false);
  savingPlayers = signal(false);
  message = signal<string>('');

  private sub?: Subscription;
  private tick?: Subscription;
  localRemaining = signal<number>(0);

  remainingDisplay = computed(() => {
    const s = this.localRemaining();
    const m = Math.floor(s / 60);
    const sec = s % 60;
    return `${m}:${sec.toString().padStart(2, '0')}`;
  });

  constructor(private http: HttpClient, private analysis: AnalysisService) {}

  ngOnInit(): void {
    this.loadPlayers();
    this.loadBlinds();
    this.loadBreak();

    this.sub = this.analysis.analysis$.subscribe(r => {
      if (!r) return;
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
    this.message.set(msg);
    setTimeout(() => { if (this.message() === msg) this.message.set(''); }, 2500);
  }

  trackBySeat = (_: number, p: PlayerRow) => p.seatNumber;
}
