import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map, startWith } from 'rxjs/operators';
import { Observable } from 'rxjs';
import { AuthService } from './services/auth.service';
import { BroadcastService } from './services/broadcast.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <ng-container *ngIf="showChrome$ | async; else bare">
      <header class="app-header">
        <div class="brand">
          <h1>RFID Poker</h1>
          <span class="broadcast-pill" [class.live]="broadcast.isLive()" [title]="broadcast.isLive() ? 'Broadcast is live' : 'Broadcast is stopped'">
            <span class="dot"></span>{{ broadcast.label() }}
          </span>
        </div>
        <nav>
          <a routerLink="/manage" routerLinkActive="active">Manage</a>
          <a routerLink="/display" routerLinkActive="active">Display</a>
          <a routerLink="/config" routerLinkActive="active">Config</a>
          <a *ngIf="auth.isAdmin()" routerLink="/admin/users" routerLinkActive="active">Users</a>
          <a *ngIf="auth.isAdmin()" routerLink="/admin/overlay-token" routerLinkActive="active">Tokens</a>
          <a *ngIf="auth.isAdmin()" routerLink="/admin/cameras" routerLinkActive="active">Cameras</a>
          <a (click)="logout()" style="cursor:pointer">Sign out</a>
        </nav>
      </header>
      <main><router-outlet></router-outlet></main>
    </ng-container>
    <ng-template #bare><router-outlet></router-outlet></ng-template>
  `,
  styles: [`
    .app-header {
      display: flex; align-items: baseline; justify-content: space-between;
      padding: 14px 24px; background: #0b1116; border-bottom: 1px solid #1c2731;
      color: #e6ecef; font-family: system-ui, -apple-system, Segoe UI, sans-serif;
    }
    .app-header .brand { display: flex; align-items: center; gap: 14px; }
    .app-header h1 { margin: 0; font-size: 20px; letter-spacing: 0.5px; color: #e6ecef; }
    .broadcast-pill {
      display: inline-flex; align-items: center; gap: 6px;
      padding: 3px 10px; border-radius: 999px; font-size: 11px;
      font-weight: 700; letter-spacing: 0.6px; text-transform: uppercase;
      background: #3a1414; color: #ff8a8a; border: 1px solid #5a1c1c;
    }
    .broadcast-pill .dot {
      width: 8px; height: 8px; border-radius: 50%; background: #ff5a5a;
    }
    .broadcast-pill.live {
      background: #103a1c; color: #7dffb0; border-color: #1c5a2f;
    }
    .broadcast-pill.live .dot {
      background: #26e070; box-shadow: 0 0 6px #26e070;
      animation: pulse 1.6s ease-in-out infinite;
    }
    @keyframes pulse { 0%,100% { opacity: 1; } 50% { opacity: 0.5; } }
    .app-header nav { display: flex; gap: 4px; }
    .app-header nav a {
      color: #7fb0c8; text-decoration: none; font-size: 14px;
      padding: 6px 12px; border-radius: 4px; transition: background 0.15s, color 0.15s;
    }
    .app-header nav a:hover { color: #b9dceb; background: #172128; }
    .app-header nav a.active { color: #ffffff; background: #1f3a4a; }
    main { display: block; }
  `]
})
export class AppComponent {
  auth = inject(AuthService);
  broadcast = inject(BroadcastService);
  showChrome$: Observable<boolean>;

  constructor(router: Router) {
    this.showChrome$ = router.events.pipe(
      filter(e => e instanceof NavigationEnd),
      startWith(null),
      map(() => {
        const url = router.url.split('?')[0];
        if (url.startsWith('/overlay') || url.startsWith('/login') || url.startsWith('/setup')) return false;
        return this.auth.isAuthenticated();
      })
    );

    // Poll broadcast status every 5s so the pill stays reasonably fresh across tabs.
    void this.broadcast.refresh();
    setInterval(() => { if (this.auth.isAuthenticated()) void this.broadcast.refresh(); }, 5000);
  }

  logout() { this.auth.logout(); }
}


